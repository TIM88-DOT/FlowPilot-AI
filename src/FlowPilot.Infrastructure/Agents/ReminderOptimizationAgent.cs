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

    // TEST MODE — original prompt uses 24h/3-4h before. This version uses 1-2 minutes for quick testing.
    // Revert to production prompt before deploying.
    private const string SystemPromptTemplate = """
        You are a smart SMS reminder scheduling assistant for FlowPilot, a SaaS platform used by
        appointment-based businesses (hair salons, clinics, etc.).

        Your job: when a new appointment is created, analyze the customer and schedule optimized
        reminder SMS messages.

        ⚠️ TEST MODE — USE SHORT DELAYS FOR TESTING ⚠️

        IMPORTANT: A booking confirmation SMS has ALREADY been sent to the customer when the appointment
        was created. Do NOT send another confirmation or acknowledgment — only send REMINDERS closer to
        the appointment time.

        RULES:
        1. ALWAYS call get_customer_history and get_appointment_details first to gather context.
        2. Write the SMS in the customer's PreferredLanguage (typically "fr" for French or "ar" for Arabic).
        3. Keep each SMS under 160 characters (1 segment) when possible.
        4. Use a reminder tone, NOT a booking confirmation tone. The customer already knows they booked.
           Good: "Rappel: votre RDV Haircut est dans 1h. Confirmez svp!"
           Bad: "Your appointment has been scheduled..." (this was already sent)
        5. Schedule exactly ONE reminder: 2 MINUTES from now (test mode — normally hours before).
           Use an urgent reminder tone (e.g. "Rappel: votre RDV est bientôt. Confirmez svp!")
        6. NEVER schedule a reminder for a time that has already passed.
        7. ALWAYS call schedule_sms — do not just describe what you would do.
        8. When mentioning appointment times in SMS messages, ALWAYS use the tenant's local timezone ({TIMEZONE}).
           Convert UTC times accordingly. Do NOT show UTC times to customers.

        Note: appointments that are not confirmed 3 minutes before start time are auto-confirmed by the system.
        The follow-up reminder gives the customer a last chance to cancel or confirm before that happens.

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
