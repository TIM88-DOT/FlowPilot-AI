using FlowPilot.Application.Appointments;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace FlowPilot.Api.Hubs;

/// <summary>
/// Pushes appointment events to SignalR clients in the matching tenant group.
/// </summary>
public sealed class AppointmentRealtimeHandler :
    INotificationHandler<AppointmentStatusChangedEvent>,
    INotificationHandler<AppointmentCreatedEvent>
{
    private readonly IHubContext<AppointmentHub> _hub;
    private readonly ILogger<AppointmentRealtimeHandler> _logger;

    public AppointmentRealtimeHandler(IHubContext<AppointmentHub> hub, ILogger<AppointmentRealtimeHandler> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task Handle(AppointmentStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Pushing AppointmentStatusChanged to tenant-{TenantId}: {AppointmentId} {OldStatus} -> {NewStatus}",
            notification.TenantId, notification.AppointmentId, notification.OldStatus, notification.NewStatus);

        await _hub.Clients.Group($"tenant-{notification.TenantId}").SendAsync(
            "AppointmentStatusChanged",
            new
            {
                notification.AppointmentId,
                notification.CustomerId,
                OldStatus = notification.OldStatus.ToString(),
                NewStatus = notification.NewStatus.ToString(),
            },
            cancellationToken);
    }

    public async Task Handle(AppointmentCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Pushing AppointmentCreated to tenant-{TenantId}: {AppointmentId}",
            notification.TenantId, notification.AppointmentId);

        await _hub.Clients.Group($"tenant-{notification.TenantId}").SendAsync(
            "AppointmentCreated",
            new
            {
                notification.AppointmentId,
                notification.CustomerId,
                notification.StartsAt,
            },
            cancellationToken);
    }
}
