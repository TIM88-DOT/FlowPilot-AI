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

    public AppointmentRealtimeHandler(IHubContext<AppointmentHub> hub)
    {
        _hub = hub;
    }

    public async Task Handle(AppointmentStatusChangedEvent notification, CancellationToken cancellationToken)
    {
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
