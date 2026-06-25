using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Messaging;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Schedules an SMS for future delivery by creating a ScheduledMessage record.
/// The ReminderDispatchWorker will pick it up at the scheduled time.
/// </summary>
public sealed class ScheduleSmsTool : IAgentTool
{
    private readonly AppDbContext _db;

    public ScheduleSmsTool(AppDbContext db) => _db = db;

    public string Name => "schedule_sms";

    public string Description =>
        "Schedules an SMS to be sent at a specific future time. Creates a pending scheduled message " +
        "that will be dispatched automatically. The body should be the final rendered SMS text.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "customerId": { "type": "string", "format": "uuid", "description": "The customer to send to" },
                "appointmentId": { "type": "string", "format": "uuid", "description": "The related appointment" },
                "body": { "type": "string", "description": "The SMS text to send. To mention the time remaining until the appointment, write the literal token {time_until} (e.g. 'Votre RDV est dans {time_until}'). NEVER write a number of hours/minutes yourself — the system fills {time_until} with the exact remaining time when the SMS is actually sent." },
                "sendAt": { "type": "string", "format": "date-time", "description": "When to send the SMS (UTC ISO 8601). MUST be in the future and strictly before the appointment start time." },
                "locale": { "type": "string", "description": "The language code used (e.g. 'fr', 'en')" }
            },
            "required": ["customerId", "appointmentId", "body", "sendAt"]
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        JsonElement root = doc.RootElement;

        Guid customerId = Guid.Parse(root.GetProperty("customerId").GetString()!);
        Guid appointmentId = Guid.Parse(root.GetProperty("appointmentId").GetString()!);
        string body = root.GetProperty("body").GetString()!.Replace("\0", string.Empty);
        DateTime sendAt = DateTime.Parse(root.GetProperty("sendAt").GetString()!).ToUniversalTime();
        string? locale = root.TryGetProperty("locale", out JsonElement localeEl)
            ? localeEl.GetString()
            : null;

        // Deterministic guards (C# is the source of truth — the LLM cannot be trusted with these):
        // 1) the appointment must exist for this tenant (also prevents a hallucinated id → FK crash).
        DateTime startsAt = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => (DateTime?)a.StartsAt)
            .FirstOrDefaultAsync(cancellationToken) ?? default;

        if (startsAt == default)
            return Error($"No appointment found with id {appointmentId} for this tenant. Do not schedule.");

        // 2) sendAt must be in the future and strictly before the appointment start.
        DateTime now = DateTime.UtcNow;
        if (sendAt <= now)
            return Error($"sendAt {sendAt:o} is in the past (now is {now:o}). Pick a future time, or skip this reminder if no valid time remains.");
        if (sendAt >= startsAt)
            return Error($"sendAt {sendAt:o} is at or after the appointment start {startsAt:o}. A reminder must be sent before the appointment.");

        // 3) the body must not hardcode a duration — it must use the {time_until} token, which the
        //    dispatcher resolves from the live appointment time when the SMS is actually sent.
        if (ReminderTimePhrase.HasHardcodedDuration(body))
            return Error("The body hardcodes a duration (e.g. 'dans 3h'). Replace the number with the literal token {time_until} so the system can fill in the correct remaining time at send.");

        var scheduledMessage = new ScheduledMessage
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            AppointmentId = appointmentId,
            Status = ScheduledMessageStatus.Pending,
            ScheduledAt = sendAt,
            RenderedBody = body,
            Locale = locale
        };

        _db.ScheduledMessages.Add(scheduledMessage);
        await _db.SaveChangesAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            success = true,
            scheduledMessageId = scheduledMessage.Id,
            scheduledAt = sendAt
        });
    }

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { success = false, error = message });
}
