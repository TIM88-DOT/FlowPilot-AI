using FlowPilot.Application.Messaging;
using FlowPilot.Infrastructure.Agents;
using MediatR;

namespace FlowPilot.Api.Hubs;

/// <summary>
/// Triggers the ReplyHandlingAgent for intent classification when an inbound SMS arrives.
/// Lives in the API assembly because the agent depends on API-only services (Azure OpenAI ChatClient).
///
/// Hub fan-out for inbound SMS is handled separately by SmsRealtimeBridge in Infrastructure,
/// which goes through IRealtimeNotifier → pg_notify → PostgresRealtimeListener → SmsHub.
/// </summary>
public sealed class SmsRealtimeHandler : INotificationHandler<InboundSmsReceivedEvent>
{
    private readonly ReplyHandlingAgent _replyAgent;
    private readonly ILogger<SmsRealtimeHandler> _logger;

    public SmsRealtimeHandler(
        ReplyHandlingAgent replyAgent,
        ILogger<SmsRealtimeHandler> logger)
    {
        _replyAgent = replyAgent;
        _logger = logger;
    }

    public async Task Handle(InboundSmsReceivedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            IntentClassification? classification = await _replyAgent.ClassifyAndActAsync(
                notification.CustomerId, notification.Body, cancellationToken);

            if (classification is not null)
            {
                _logger.LogInformation(
                    "ReplyHandlingAgent classified message {MessageId}: intent={Intent}, confidence={Confidence:F2}",
                    notification.MessageId, classification.Intent, classification.Confidence);
            }
        }
        catch (Exception ex)
        {
            // Agent failure must not break inbound SMS processing
            _logger.LogWarning(ex,
                "ReplyHandlingAgent failed for message {MessageId} — inbound SMS was still saved",
                notification.MessageId);
        }
    }
}
