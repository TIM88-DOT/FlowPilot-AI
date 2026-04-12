using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;

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
                "body": { "type": "string", "description": "The SMS text to send" },
                "sendAt": { "type": "string", "format": "date-time", "description": "When to send the SMS (UTC ISO 8601)" },
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
}
