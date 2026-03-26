using FlowPilot.Application.Agents;
using FlowPilot.Application.Appointments;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Agents;

/// <summary>
/// When an appointment is created, this agent analyzes the customer's history
/// and schedules an optimized reminder SMS at the best time and in the right language.
/// </summary>
public sealed class ReminderOptimizationAgent : INotificationHandler<AppointmentCreatedEvent>
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<ReminderOptimizationAgent> _logger;

    private static readonly string[] ToolNames =
    [
        "get_customer_history",
        "get_appointment_details",
        "schedule_sms"
    ];

    private const string SystemPrompt = """
        You are a smart SMS reminder scheduling assistant for FlowPilot, a SaaS platform used by
        appointment-based businesses in Algeria (hair salons, clinics, etc.).

        Your job: when a new appointment is created, analyze the customer and schedule an optimized
        reminder SMS.

        RULES:
        1. ALWAYS call get_customer_history and get_appointment_details first to gather context.
        2. Write the SMS in the customer's PreferredLanguage (typically "fr" for French or "ar" for Arabic).
        3. Keep the SMS under 160 characters (1 segment) when possible.
        4. Include: business name context, date/time, service name, and a confirmation prompt
           (e.g. "Répondez OUI pour confirmer").
        5. Schedule the reminder at an appropriate time:
           - For appointments tomorrow: send the evening before (around 18:00-19:00 local time)
           - For appointments 2+ days away: send 24h before, during morning hours (9:00-10:00)
           - For customers with high no-show scores (>0.3): send an additional early reminder 48h before
        6. NEVER schedule a reminder for a time that has already passed.
        7. ALWAYS call schedule_sms to actually schedule the message — do not just describe what you would do.

        The current timezone context is Africa/Algiers (UTC+1).
        """;

    public ReminderOptimizationAgent(IAgentOrchestrator orchestrator, ILogger<ReminderOptimizationAgent> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task Handle(AppointmentCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ReminderOptimizationAgent triggered for appointment {AppointmentId}, customer {CustomerId}",
            notification.AppointmentId, notification.CustomerId);

        string userMessage = $"A new appointment has been created. " +
            $"Appointment ID: {notification.AppointmentId}. " +
            $"Customer ID: {notification.CustomerId}. " +
            $"Please analyze the customer and schedule an optimized reminder SMS.";

        AgentRunResult result = await _orchestrator.RunAsync(new AgentRequest(
            AgentType: "ReminderOptimization",
            SystemPrompt: SystemPrompt,
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
