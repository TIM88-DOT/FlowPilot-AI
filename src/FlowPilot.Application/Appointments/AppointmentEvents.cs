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
    Guid CustomerId,
    Guid TenantId,
    Guid? UserId,
    AppointmentStatus OldStatus,
    AppointmentStatus NewStatus) : INotification;

/// <summary>
/// Published when an appointment is marked as Missed (no-show).
/// Downstream handlers may send a "we missed you" SMS, update analytics, or trigger no-show fees.
/// </summary>
public sealed record AppointmentMissedEvent(
    Guid AppointmentId,
    Guid CustomerId,
    Guid TenantId,
    DateTime StartsAt) : INotification;
