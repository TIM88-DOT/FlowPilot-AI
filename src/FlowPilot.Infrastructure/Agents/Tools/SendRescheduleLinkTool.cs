using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Sends an SMS to the customer containing a link to the public booking page in "reschedule mode".
///
/// We deliberately do NOT parse dates from SMS — that is brittle across natural language.
/// Instead, the customer clicks the link, picks a new slot on the public booking UI, and the
/// backend calls IAppointmentService.RescheduleAsync. The original appointment is the one
/// identified here by appointmentId and remains in "Scheduled/Confirmed" until the customer
/// completes the new selection.
/// </summary>
public sealed class SendRescheduleLinkTool : IAgentTool
{
    private readonly IMessagingService _messagingService;
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IConfiguration _configuration;

    public SendRescheduleLinkTool(
        IMessagingService messagingService,
        AppDbContext db,
        ICurrentTenant currentTenant,
        IConfiguration configuration)
    {
        _messagingService = messagingService;
        _db = db;
        _currentTenant = currentTenant;
        _configuration = configuration;
    }

    public string Name => "send_reschedule_link";

    public string Description =>
        "Sends the customer an SMS containing a link to reschedule their appointment. " +
        "Use this when the customer's intent is to reschedule and you are confident (>=0.85). " +
        "Do NOT attempt to parse a new date from the SMS — the customer picks a new slot on the web.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "appointmentId": { "type": "string", "format": "uuid", "description": "The appointment the customer wants to reschedule" },
                "language": {
                    "type": "string",
                    "enum": ["fr", "en"],
                    "description": "Language the customer replied in. Use 'fr' for French, 'en' for English. Defaults to the customer's preferred language if unsure."
                }
            },
            "required": ["appointmentId"]
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        JsonElement root = doc.RootElement;
        Guid appointmentId = Guid.Parse(root.GetProperty("appointmentId").GetString()!);
        string? language = root.TryGetProperty("language", out JsonElement langEl) && langEl.ValueKind == JsonValueKind.String
            ? langEl.GetString()
            : null;

        Appointment? appointment = await _db.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment is null)
            return JsonSerializer.Serialize(new { success = false, error = $"Appointment {appointmentId} not found." });

        Customer? customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == appointment.CustomerId, cancellationToken);

        Tenant? tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _currentTenant.TenantId, cancellationToken);

        if (tenant is null || string.IsNullOrWhiteSpace(tenant.Slug))
            return JsonSerializer.Serialize(new { success = false, error = "Tenant slug missing — cannot build reschedule link." });

        string baseUrl = _configuration["PublicBaseUrl"]?.TrimEnd('/') ?? "https://app.flowpilot.ai";
        string rescheduleUrl = $"{baseUrl}/book/{tenant.Slug}?reschedule={appointment.Id}";

        // Format the current appointment time in the tenant's timezone for the SMS body
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone ?? "UTC");
        DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(appointment.StartsAt, tz);
        string when = localStart.ToString("dd/MM HH:mm");

        // Fall back to customer preference when the agent didn't supply a language hint.
        string resolvedLang = (language ?? customer?.PreferredLanguage ?? "fr").ToLowerInvariant();
        string body = resolvedLang switch
        {
            "en" => $"Hi, to reschedule your appointment on {when} at {tenant.BusinessName}, " +
                    $"pick a new time here: {rescheduleUrl}",
            _ => $"Bonjour, pour reprogrammer votre rendez-vous du {when} chez {tenant.BusinessName}, " +
                 $"choisissez un nouveau créneau ici : {rescheduleUrl}"
        };

        var request = new SendRawSmsRequest(appointment.CustomerId, body);
        Result<SendSmsResponse> result = await _messagingService.SendRawAsync(request, cancellationToken);

        if (result.IsFailure)
            return JsonSerializer.Serialize(new { success = false, error = result.Error.Description });

        return JsonSerializer.Serialize(new
        {
            success = true,
            messageId = result.Value.MessageId,
            rescheduleUrl
        });
    }
}
