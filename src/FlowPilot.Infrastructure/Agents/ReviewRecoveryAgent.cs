using FlowPilot.Application.Agents;
using FlowPilot.Application.Appointments;
using FlowPilot.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Agents;

/// <summary>
/// When an appointment is completed, this agent checks review platform configuration
/// and cooldown, then sends a polite review request SMS with the appropriate platform link.
///
/// Hard gates enforced in C# (via tools):
/// - Review platform must be configured (get_review_platforms)
/// - 30-day cooldown must not be active (check_review_cooldown)
/// - Customer must be opted-in (enforced by send_sms → MessagingService consent gate)
/// </summary>
public sealed class ReviewRecoveryAgent : INotificationHandler<AppointmentStatusChangedEvent>
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<ReviewRecoveryAgent> _logger;

    private static readonly string[] ToolNames =
    [
        "get_customer_history",
        "get_review_platforms",
        "check_review_cooldown",
        "send_sms"
    ];

    private const string SystemPrompt = """
        You are a review request assistant for FlowPilot, a SaaS platform used by appointment-based
        businesses in Algeria.

        Your job: after a completed appointment, send a polite review request SMS to the customer
        with the appropriate review platform link.

        RULES:
        1. ALWAYS call get_review_platforms first to check if the business has configured any review platform.
           If no platform is configured, do NOT send any SMS — just respond that no platforms are configured.
        2. ALWAYS call check_review_cooldown to verify the customer hasn't received a review request recently.
           If canSendReview is false, do NOT send — just respond that the cooldown is active.
        3. Call get_customer_history to determine the customer's preferred language.
        4. Write the SMS in the customer's PreferredLanguage (typically "fr" or "ar").
        5. Keep the SMS warm, brief, and professional. Include:
           - A thank you for their visit
           - A polite review request
           - The review platform link (prefer Google if available)
        6. Keep under 160 characters if possible.
        7. Only call send_sms if BOTH conditions are met:
           - A review platform is configured
           - The cooldown check passed (canSendReview = true)

        Example (French):
        "Merci pour votre visite ! Votre avis compte beaucoup pour nous 😊 {link}"
        """;

    public ReviewRecoveryAgent(IAgentOrchestrator orchestrator, ILogger<ReviewRecoveryAgent> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task Handle(AppointmentStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        // Only trigger on completion
        if (notification.NewStatus != AppointmentStatus.Completed)
            return;

        _logger.LogInformation(
            "ReviewRecoveryAgent triggered for completed appointment {AppointmentId}, customer {CustomerId}",
            notification.AppointmentId, notification.CustomerId);

        string userMessage = $"Appointment {notification.AppointmentId} for customer {notification.CustomerId} " +
            "has been completed. Check if we should send a review request SMS.";

        AgentRunResult result = await _orchestrator.RunAsync(new AgentRequest(
            AgentType: "ReviewRecovery",
            SystemPrompt: SystemPrompt,
            UserMessage: userMessage,
            ToolNames: ToolNames,
            AppointmentId: notification.AppointmentId,
            CustomerId: notification.CustomerId,
            TriggerEvent: "AppointmentCompleted"
        ), cancellationToken);

        if (!result.Success)
        {
            _logger.LogError(
                "ReviewRecoveryAgent failed for appointment {AppointmentId}: {Error}",
                notification.AppointmentId, result.ErrorMessage);
        }
    }
}
