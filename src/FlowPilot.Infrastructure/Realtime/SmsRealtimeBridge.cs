using FlowPilot.Application.Messaging;
using FlowPilot.Application.Realtime;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Realtime;

/// <summary>
/// Bridges inbound SMS events to the realtime hub layer so the inbox UI updates live.
/// Lives alongside AppointmentRealtimeBridge in Infrastructure for uniform fan-out —
/// all hub pushes go through IRealtimeNotifier regardless of which process publishes the event.
/// </summary>
public sealed class SmsRealtimeBridge : INotificationHandler<InboundSmsReceivedEvent>
{
    private const string HubName = "sms";

    private readonly IRealtimeNotifier _notifier;
    private readonly ILogger<SmsRealtimeBridge> _logger;

    public SmsRealtimeBridge(IRealtimeNotifier notifier, ILogger<SmsRealtimeBridge> logger)
    {
        _notifier = notifier;
        _logger = logger;
    }

    public Task Handle(InboundSmsReceivedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Realtime fan-out: NewInboundSms {MessageId} from {FromPhone} (tenant {TenantId})",
            notification.MessageId, notification.FromPhone, notification.TenantId);

        return _notifier.PublishAsync(
            notification.TenantId,
            HubName,
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
    }
}
