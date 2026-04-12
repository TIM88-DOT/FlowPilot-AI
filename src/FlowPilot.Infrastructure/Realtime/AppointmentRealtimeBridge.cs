using FlowPilot.Application.Appointments;
using FlowPilot.Application.Realtime;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Realtime;

/// <summary>
/// Bridges appointment domain events to the realtime hub layer.
/// Lives in Infrastructure so both API and Workers pick it up via MediatR assembly scanning —
/// that's the whole point: when the AppointmentLifecycleWorker transitions a Scheduled→Missed
/// appointment, this handler fans the event out to browser clients via pg_notify.
/// </summary>
public sealed class AppointmentRealtimeBridge :
    INotificationHandler<AppointmentStatusChangedEvent>,
    INotificationHandler<AppointmentCreatedEvent>
{
    private const string HubName = "appointments";

    private readonly IRealtimeNotifier _notifier;
    private readonly ILogger<AppointmentRealtimeBridge> _logger;

    public AppointmentRealtimeBridge(IRealtimeNotifier notifier, ILogger<AppointmentRealtimeBridge> logger)
    {
        _notifier = notifier;
        _logger = logger;
    }

    public Task Handle(AppointmentStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Realtime fan-out: AppointmentStatusChanged {AppointmentId} {OldStatus} → {NewStatus} (tenant {TenantId})",
            notification.AppointmentId, notification.OldStatus, notification.NewStatus, notification.TenantId);

        return _notifier.PublishAsync(
            notification.TenantId,
            HubName,
            "AppointmentStatusChanged",
            new
            {
                notification.AppointmentId,
                notification.CustomerId,
                OldStatus = notification.OldStatus.ToString(),
                NewStatus = notification.NewStatus.ToString()
            },
            cancellationToken);
    }

    public Task Handle(AppointmentCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Realtime fan-out: AppointmentCreated {AppointmentId} (tenant {TenantId})",
            notification.AppointmentId, notification.TenantId);

        return _notifier.PublishAsync(
            notification.TenantId,
            HubName,
            "AppointmentCreated",
            new
            {
                notification.AppointmentId,
                notification.CustomerId,
                notification.StartsAt
            },
            cancellationToken);
    }
}
