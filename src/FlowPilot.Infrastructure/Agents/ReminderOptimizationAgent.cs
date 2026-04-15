using FlowPilot.Application.Agents;
using FlowPilot.Application.Appointments;
using FlowPilot.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Agents;

/// <summary>
/// When an appointment is created, this agent analyzes the customer's history
/// and schedules an optimized reminder SMS at the best time and in the right language.
/// </summary>
public sealed class ReminderOptimizationAgent : INotificationHandler<AppointmentCreatedEvent>
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly AppDbContext _db;
    private readonly ILogger<ReminderOptimizationAgent> _logger;

    private static readonly string[] ToolNames =
    [
        "get_customer_history",
        "get_appointment_details",
        "schedule_sms"
    ];

    private const string SystemPromptTemplate = """
        You are a smart SMS reminder scheduling assistant for FlowPilot, a SaaS platform used by
        appointment-based businesses (hair salons, clinics, etc.).

        Your job: when a new appointment is created, analyze the customer and schedule up to two optimized
        reminder SMS messages that ask for confirmation.

        IMPORTANT: A booking confirmation SMS has ALREADY been sent to the customer when the appointment
        was created. Do NOT send another confirmation or acknowledgment — only send REMINDERS closer to
        the appointment time.

        RULES:
        1. ALWAYS call get_customer_history and get_appointment_details first to gather context.
        2. Write each SMS in the customer's PreferredLanguage ("fr" for French or "en" for English).
        3. Keep each SMS under 160 characters (1 segment) when possible.
        4. Use a reminder tone, NOT a booking confirmation tone. The customer already knows they booked.
           Good: "Rappel: votre RDV Haircut demain à 14h. Répondez OUI pour confirmer."
           Bad: "Your appointment has been scheduled..." (this was already sent)

        5. Schedule up to TWO reminders by calling schedule_sms. Pick sendAt carefully:
           a. FIRST reminder (friendly, asks for confirmation):
              - Preferred sendAt: StartsAt − 24 hours.
              - If StartsAt − 24h is already in the past (same-day booking), SKIP this reminder entirely.
              - Text references "tomorrow" only when sendAt really is ~24h before StartsAt.
              - Example FR: "Rappel: RDV {service} demain à {time}. Répondez OUI pour confirmer."
              - Example EN: "Reminder: {service} tomorrow at {time}. Reply YES to confirm."
           b. SECOND reminder (urgent, last chance):
              - Preferred sendAt: StartsAt − 3 hours.
              - If StartsAt − 3h is in the past, fall back to a sendAt ~30 minutes from now, but ONLY if
                there's still at least 20 minutes between that sendAt and StartsAt. Otherwise skip.
              - If the first reminder was skipped, you may schedule this urgent one at StartsAt − 2h or
                − 1h instead, so long as sendAt is in the future.

        6. ** CRITICAL: Never hardcode "3h" or any fixed duration in the urgent reminder body. **
           The body MUST describe the ACTUAL time remaining between sendAt and StartsAt, computed fresh.
           Use natural phrasing:
             - gap ≥ 2h  → "in {N}h" (e.g. "in 3h", "in 2h")
             - gap 1h–2h → "in 1h" or "in {N}h"
             - gap < 1h  → "in {M} min" (e.g. "in 45 min", "in 30 min")
           Examples (compute based on YOUR chosen sendAt, do not copy the number literally):
             FR gap=3h:   "Votre RDV est dans 3h à {time}. Merci de confirmer en répondant OUI."
             FR gap=45m:  "Votre RDV est dans 45 min à {time}. Confirmez en répondant OUI."
             EN gap=2h:   "Your {service} is in 2h at {time}. Please confirm by replying YES."
             EN gap=30m:  "Your {service} is in 30 min at {time}. Reply YES to confirm."

        7. NEVER schedule a reminder for a sendAt that has already passed.
        8. ALWAYS call schedule_sms for every reminder you plan — do not just describe what you would do.
        9. When mentioning appointment times in SMS messages, ALWAYS use the tenant's local timezone ({TIMEZONE}).
           Convert UTC times accordingly. Do NOT show UTC times to customers.

        The current timezone context is {TIMEZONE}.
        """;

    public ReminderOptimizationAgent(IAgentOrchestrator orchestrator, AppDbContext db, ILogger<ReminderOptimizationAgent> logger)
    {
        _orchestrator = orchestrator;
        _db = db;
        _logger = logger;
    }

    public async Task Handle(AppointmentCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ReminderOptimizationAgent triggered for appointment {AppointmentId}, customer {CustomerId}",
            notification.AppointmentId, notification.CustomerId);

        // Load tenant timezone for the system prompt
        Domain.Entities.Tenant? tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == notification.TenantId, cancellationToken);
        string timezone = tenant?.Timezone ?? "UTC";
        string systemPrompt = SystemPromptTemplate
            .Replace("{TIMEZONE}", timezone);

        string userMessage = $"A new appointment has been created. " +
            $"Appointment ID: {notification.AppointmentId}. " +
            $"Customer ID: {notification.CustomerId}. " +
            $"Please analyze the customer and schedule an optimized reminder SMS.";

        AgentRunResult result = await _orchestrator.RunAsync(new AgentRequest(
            AgentType: "ReminderOptimization",
            SystemPrompt: systemPrompt,
            UserMessage: userMessage,
            ToolNames: ToolNames,
            AppointmentId: notification.AppointmentId,
            CustomerId: notification.CustomerId,
            TriggerEvent: "AppointmentCreated"
        ), cancellationToken);

        if (!result.Success)
        {
            _logger.LogError(
                "ReminderOptimizationAgent failed for appointment {AppointmentId}: {Error}",
                notification.AppointmentId, result.ErrorMessage);
        }
    }
}
