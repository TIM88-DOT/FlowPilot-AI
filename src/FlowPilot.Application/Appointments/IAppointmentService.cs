using FlowPilot.Application.Customers;
using FlowPilot.Shared;

namespace FlowPilot.Application.Appointments;

/// <summary>
/// Appointment CRUD, status transitions, webhook ingestion, and CSV import.
/// </summary>
public interface IAppointmentService
{
    /// <summary>
    /// Creates a new appointment and publishes an AppointmentCreated event.
    /// </summary>
    Task<Result<AppointmentDto>> CreateAsync(CreateAppointmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paginated, filtered list of appointments.
    /// </summary>
    Task<Result<PagedResult<AppointmentDto>>> ListAsync(AppointmentQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single appointment by id.
    /// </summary>
    Task<Result<AppointmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions appointment to Confirmed.
    /// </summary>
    Task<Result<AppointmentDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions appointment to Cancelled.
    /// </summary>
    Task<Result<AppointmentDto>> CancelAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions appointment to Completed.
    /// </summary>
    Task<Result<AppointmentDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions appointment to Missed (no-show) and increments the customer's NoShowScore.
    /// </summary>
    Task<Result<AppointmentDto>> MarkMissedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions appointment to Rescheduled with new times, then creates a new Scheduled appointment.
    /// </summary>
    Task<Result<AppointmentDto>> RescheduleAsync(Guid id, RescheduleAppointmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotent webhook ingestion. Uses ExternalId + TenantId to prevent duplicates.
    /// </summary>
    Task<Result<AppointmentDto>> IngestFromWebhookAsync(InboundAppointmentWebhook webhook, CancellationToken cancellationToken = default);
}
