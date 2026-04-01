using FlowPilot.Application.Messaging;
using FlowPilot.Infrastructure.Agents;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace FlowPilot.Api.Hubs;

/// <summary>
/// Handles inbound SMS events: pushes to SignalR for real-time inbox updates
/// and triggers the ReplyHandlingAgent for intent classification.
/// </summary>
public sealed class SmsRealtimeHandler : INotificationHandler<InboundSmsReceivedEvent>
{
    private readonly IHubContext<SmsHub> _hub;
    private readonly ReplyHandlingAgent _replyAgent;
    private readonly ILogger<SmsRealtimeHandler> _logger;

    public SmsRealtimeHandler(
        IHubContext<SmsHub> hub,
        ReplyHandlingAgent replyAgent,
        ILogger<SmsRealtimeHandler> logger)
    {
        _hub = hub;
        _replyAgent = replyAgent;
        _logger = logger;
    }

    public async Task Handle(InboundSmsReceivedEvent notification, CancellationToken cancellationToken)
    {
        // Push to SignalR so the inbox UI refreshes immediately
        _logger.LogInformation(
            "Pushing NewInboundSms to tenant-{TenantId}: message {MessageId} from {FromPhone}",
            notification.TenantId, notification.MessageId, notification.FromPhone);

        await _hub.Clients.Group($"tenant-{notification.TenantId}").SendAsync(
            "NewInboundSms",
            new
            {
                notification.MessageId,
                notification.CustomerId,
                notification.Body,
                notification.FromPhone,
                ReceivedAt = DateTime.UtcNow
            },
            cancellationToken);

        // Trigger ReplyHandlingAgent for intent classification (fire-and-forget semantics)
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
