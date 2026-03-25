using FlowPilot.Domain.Enums;
using MediatR;

namespace FlowPilot.Application.Appointments;

/// <summary>
/// Published when a new appointment is created. Handlers can trigger reminder scheduling.
/// </summary>
public sealed record AppointmentCreatedEvent(
    Guid AppointmentId,
    Guid CustomerId,
    Guid TenantId,
    DateTime StartsAt) : INotification;

/// <summary>
/// Published when an appointment's status changes. Handlers create audit log entries.
/// </summary>
public sealed record AppointmentStatusChangedEvent(
    Guid AppointmentId,
    Guid TenantId,
    Guid? UserId,
    AppointmentStatus OldStatus,
    AppointmentStatus NewStatus) : INotification;
