using FlowPilot.Application.Appointments;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using MediatR;

namespace FlowPilot.Infrastructure.Appointments;

/// <summary>
/// Creates an audit log entry whenever an appointment's status changes.
/// </summary>
public sealed class AppointmentStatusChangedHandler : INotificationHandler<AppointmentStatusChangedEvent>
{
    private readonly AppDbContext _db;

    public AppointmentStatusChangedHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(AppointmentStatusChangedEvent notification, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog
        {
            AppointmentId = notification.AppointmentId,
            UserId = notification.UserId,
            EntityType = "Appointment",
            EntityId = notification.AppointmentId,
            Action = $"StatusChanged:{notification.OldStatus}->{notification.NewStatus}",
            OldValue = $"\"{notification.OldStatus}\"",
            NewValue = $"\"{notification.NewStatus}\""
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
