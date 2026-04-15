using FlowPilot.Domain.Enums;

namespace FlowPilot.Application.Appointments;

/// <summary>
/// Request to create a new appointment.
/// </summary>
public sealed record CreateAppointmentRequest(
    Guid CustomerId,
    DateTime StartsAt,
    DateTime EndsAt,
    string? ServiceName = null,
    string? Notes = null,
    Guid? StaffUserId = null);

/// <summary>
/// Request to reschedule an existing appointment.
/// </summary>
public sealed record RescheduleAppointmentRequest(
    DateTime StartsAt,
    DateTime EndsAt);

/// <summary>
/// Query parameters for listing appointments.
/// </summary>
public sealed record AppointmentQuery(
    AppointmentStatus? Status = null,
    Guid? StaffUserId = null,
    Guid? CustomerId = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    string? Search = null,
    bool? AtRisk = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>
/// Appointment data returned to callers.
/// </summary>
public sealed record AppointmentDto(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    Guid? StaffUserId,
    string? ExternalId,
    string Status,
    DateTime StartsAt,
    DateTime EndsAt,
    string? ServiceName,
    string? Notes,
    DateTime? AtRiskAlertedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Inbound webhook payload for external booking systems.
/// Idempotent on ExternalId + TenantId.
/// </summary>
public sealed record InboundAppointmentWebhook(
    string ExternalId,
    Guid CustomerId,
    DateTime StartsAt,
    DateTime EndsAt,
    string? ServiceName = null,
    string? Notes = null,
    Guid? StaffUserId = null);
