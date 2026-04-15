using FlowPilot.Application.Appointments;
using FlowPilot.Application.Customers;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Appointments;

/// <summary>
/// Implements appointment CRUD with domain-enforced status transitions.
/// Publishes MediatR events for audit logging and downstream processing.
/// </summary>
public sealed class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IMediator _mediator;

    /// <summary>
    /// Valid status transitions enforced by the domain.
    /// Key = current status, Value = set of statuses it can transition to.
    /// </summary>
    private static readonly Dictionary<AppointmentStatus, HashSet<AppointmentStatus>> ValidTransitions = new()
    {
        [AppointmentStatus.Scheduled] = [AppointmentStatus.Confirmed, AppointmentStatus.Cancelled, AppointmentStatus.Missed, AppointmentStatus.Rescheduled],
        [AppointmentStatus.Confirmed] = [AppointmentStatus.Completed, AppointmentStatus.Cancelled, AppointmentStatus.Missed, AppointmentStatus.Rescheduled],
        [AppointmentStatus.Cancelled] = [],
        [AppointmentStatus.Missed] = [],
        [AppointmentStatus.Completed] = [],
        [AppointmentStatus.Rescheduled] = [],
    };

    public AppointmentService(AppDbContext db, ICurrentTenant currentTenant, IMediator mediator)
    {
        _db = db;
        _currentTenant = currentTenant;
        _mediator = mediator;
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentDto>> CreateAsync(CreateAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        // Verify the customer exists in this tenant
        bool customerExists = await _db.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CustomerId, cancellationToken);

        if (!customerExists)
            return Result.Failure<AppointmentDto>(Error.Validation("Appointment.CustomerNotFound", "The specified customer was not found."));

        if (request.EndsAt <= request.StartsAt)
            return Result.Failure<AppointmentDto>(Error.Validation("Appointment.InvalidTimeRange", "End time must be after start time."));

        var appointment = new Appointment
        {
            CustomerId = request.CustomerId,
            StaffUserId = request.StaffUserId,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            ServiceName = request.ServiceName,
            Notes = request.Notes,
            Status = AppointmentStatus.Scheduled
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync(cancellationToken);

        await _mediator.Publish(new AppointmentCreatedEvent(
            appointment.Id, appointment.CustomerId, _currentTenant.TenantId, appointment.StartsAt), cancellationToken);

        await _mediator.Publish(new AppointmentStatusChangedEvent(
            appointment.Id, appointment.CustomerId, _currentTenant.TenantId, _currentTenant.UserId,
            AppointmentStatus.Scheduled, AppointmentStatus.Scheduled), cancellationToken);

        return Result.Success(await ToDtoAsync(appointment, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<AppointmentDto>>> ListAsync(AppointmentQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<Appointment> q = _db.Appointments
            .AsNoTracking()
            .Include(a => a.Customer);

        if (query.Status.HasValue)
            q = q.Where(a => a.Status == query.Status.Value);

        if (query.StaffUserId.HasValue)
            q = q.Where(a => a.StaffUserId == query.StaffUserId.Value);

        if (query.CustomerId.HasValue)
            q = q.Where(a => a.CustomerId == query.CustomerId.Value);

        if (query.DateFrom.HasValue)
            q = q.Where(a => a.StartsAt >= query.DateFrom.Value);

        if (query.DateTo.HasValue)
            q = q.Where(a => a.StartsAt <= query.DateTo.Value);

        // At-risk pseudo-filter: Scheduled appointments the lifecycle worker flagged as unconfirmed.
        // Overrides any Status filter the caller may have passed.
        if (query.AtRisk == true)
        {
            q = q.Where(a => a.Status == AppointmentStatus.Scheduled && a.AtRiskAlertedAt != null);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.Trim().ToLower();
            q = q.Where(a =>
                a.Customer.FirstName.ToLower().Contains(term)
                || (a.Customer.LastName != null && a.Customer.LastName.ToLower().Contains(term))
                || (a.ServiceName != null && a.ServiceName.ToLower().Contains(term)));
        }

        int totalCount = await q.CountAsync(cancellationToken);

        // Sort at-risk appointments first so staff can act on them quickly, then newest StartsAt.
        List<AppointmentDto> items = await q
            .OrderByDescending(a => a.Status == AppointmentStatus.Scheduled && a.AtRiskAlertedAt != null)
            .ThenByDescending(a => a.StartsAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new AppointmentDto(
                a.Id, a.CustomerId,
                a.Customer.FirstName + (a.Customer.LastName != null ? " " + a.Customer.LastName : ""),
                a.StaffUserId, a.ExternalId,
                a.Status.ToString(), a.StartsAt, a.EndsAt,
                a.ServiceName, a.Notes, a.AtRiskAlertedAt, a.CreatedAt, a.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<AppointmentDto>(items, totalCount, query.Page, query.PageSize));
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Appointment? appointment = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (appointment is null)
            return Result.Failure<AppointmentDto>(Error.NotFound("Appointment", id));

        return Result.Success(ToDto(appointment));
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await TransitionAsync(id, AppointmentStatus.Confirmed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentDto>> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await TransitionAsync(id, AppointmentStatus.Cancelled, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await TransitionAsync(id, AppointmentStatus.Completed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentDto>> MarkMissedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Appointment? appointment = await _db.Appointments
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (appointment is null)
            return Result.Failure<AppointmentDto>(Error.NotFound("Appointment", id));

        Result<AppointmentDto>? validationError = ValidateTransition(appointment, AppointmentStatus.Missed);
        if (validationError is not null && validationError.IsFailure)
            return validationError;

        AppointmentStatus oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.Missed;

        // Increment no-show score on the customer (capped at 1.0)
        appointment.Customer.NoShowScore = Math.Min(1.0m, appointment.Customer.NoShowScore + 0.1m);

        await _db.SaveChangesAsync(cancellationToken);

        await _mediator.Publish(new AppointmentStatusChangedEvent(
            appointment.Id, appointment.CustomerId, _currentTenant.TenantId, _currentTenant.UserId,
            oldStatus, AppointmentStatus.Missed), cancellationToken);

        await _mediator.Publish(new AppointmentMissedEvent(
            appointment.Id, appointment.CustomerId, _currentTenant.TenantId, appointment.StartsAt), cancellationToken);

        return Result.Success(ToDto(appointment));
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentDto>> RescheduleAsync(Guid id, RescheduleAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.EndsAt <= request.StartsAt)
            return Result.Failure<AppointmentDto>(Error.Validation("Appointment.InvalidTimeRange", "End time must be after start time."));

        Appointment? appointment = await _db.Appointments
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (appointment is null)
            return Result.Failure<AppointmentDto>(Error.NotFound("Appointment", id));

        Result<AppointmentDto>? transitionResult = ValidateTransition(appointment, AppointmentStatus.Rescheduled);
        if (transitionResult is not null && transitionResult.IsFailure)
            return transitionResult;

        AppointmentStatus oldStatus = appointment.Status;
        appointment.Status = AppointmentStatus.Rescheduled;
        appointment.Notes = $"Rescheduled. Original: {appointment.StartsAt:g} – {appointment.EndsAt:g}. {appointment.Notes}".Trim();

        // Create new appointment with the new times
        var newAppointment = new Appointment
        {
            CustomerId = appointment.CustomerId,
            StaffUserId = appointment.StaffUserId,
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            ServiceName = appointment.ServiceName,
            Status = AppointmentStatus.Scheduled
        };

        _db.Appointments.Add(newAppointment);
        await _db.SaveChangesAsync(cancellationToken);

        // Publish events for old appointment (status changed to Rescheduled)
        await _mediator.Publish(new AppointmentStatusChangedEvent(
            appointment.Id, appointment.CustomerId, _currentTenant.TenantId, _currentTenant.UserId,
            oldStatus, AppointmentStatus.Rescheduled), cancellationToken);

        // Publish events for new appointment (created + scheduled)
        await _mediator.Publish(new AppointmentCreatedEvent(
            newAppointment.Id, newAppointment.CustomerId, _currentTenant.TenantId, newAppointment.StartsAt), cancellationToken);

        await _mediator.Publish(new AppointmentStatusChangedEvent(
            newAppointment.Id, newAppointment.CustomerId, _currentTenant.TenantId, _currentTenant.UserId,
            AppointmentStatus.Scheduled, AppointmentStatus.Scheduled), cancellationToken);

        // Return the NEW appointment
        return Result.Success(await ToDtoAsync(newAppointment, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentDto>> IngestFromWebhookAsync(InboundAppointmentWebhook webhook, CancellationToken cancellationToken = default)
    {
        // Idempotency: check if this ExternalId already exists for this tenant
        Appointment? existing = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.ExternalId == webhook.ExternalId, cancellationToken);

        if (existing is not null)
            return Result.Success(ToDto(existing));

        // Verify the customer exists
        bool customerExists = await _db.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == webhook.CustomerId, cancellationToken);

        if (!customerExists)
            return Result.Failure<AppointmentDto>(Error.Validation("Appointment.CustomerNotFound", "The specified customer was not found."));

        if (webhook.EndsAt <= webhook.StartsAt)
            return Result.Failure<AppointmentDto>(Error.Validation("Appointment.InvalidTimeRange", "End time must be after start time."));

        var appointment = new Appointment
        {
            ExternalId = webhook.ExternalId,
            CustomerId = webhook.CustomerId,
            StaffUserId = webhook.StaffUserId,
            StartsAt = webhook.StartsAt,
            EndsAt = webhook.EndsAt,
            ServiceName = webhook.ServiceName,
            Notes = webhook.Notes,
            Status = AppointmentStatus.Scheduled
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync(cancellationToken);

        await _mediator.Publish(new AppointmentCreatedEvent(
            appointment.Id, appointment.CustomerId, _currentTenant.TenantId, appointment.StartsAt), cancellationToken);

        await _mediator.Publish(new AppointmentStatusChangedEvent(
            appointment.Id, appointment.CustomerId, _currentTenant.TenantId, _currentTenant.UserId,
            AppointmentStatus.Scheduled, AppointmentStatus.Scheduled), cancellationToken);

        return Result.Success(await ToDtoAsync(appointment, cancellationToken));
    }

    // -----------------------------------------------------------------------
    // Status transition logic
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validates and applies a status transition, publishes events, and returns the updated DTO.
    /// </summary>
    private async Task<Result<AppointmentDto>> TransitionAsync(Guid id, AppointmentStatus newStatus, CancellationToken cancellationToken)
    {
        Appointment? appointment = await _db.Appointments
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (appointment is null)
            return Result.Failure<AppointmentDto>(Error.NotFound("Appointment", id));

        Result<AppointmentDto>? validationError = ValidateTransition(appointment, newStatus);
        if (validationError is not null && validationError.IsFailure)
            return validationError;

        AppointmentStatus oldStatus = appointment.Status;
        appointment.Status = newStatus;
        await _db.SaveChangesAsync(cancellationToken);

        await _mediator.Publish(new AppointmentStatusChangedEvent(
            appointment.Id, appointment.CustomerId, _currentTenant.TenantId, _currentTenant.UserId,
            oldStatus, newStatus), cancellationToken);

        return Result.Success(ToDto(appointment));
    }

    /// <summary>
    /// Checks if a transition from the appointment's current status to newStatus is valid.
    /// Returns a failure Result if invalid, null if valid.
    /// </summary>
    private static Result<AppointmentDto>? ValidateTransition(Appointment appointment, AppointmentStatus newStatus)
    {
        if (!ValidTransitions.TryGetValue(appointment.Status, out HashSet<AppointmentStatus>? allowed) || !allowed.Contains(newStatus))
        {
            return Result.Failure<AppointmentDto>(Error.Validation(
                "Appointment.InvalidTransition",
                $"Cannot transition from '{appointment.Status}' to '{newStatus}'."));
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Mapping
    // -----------------------------------------------------------------------

    /// <summary>
    /// Maps an appointment to DTO. Use when Customer navigation is already loaded.
    /// </summary>
    private static AppointmentDto ToDto(Appointment a) =>
        new(a.Id, a.CustomerId,
            a.Customer.FirstName + (a.Customer.LastName != null ? " " + a.Customer.LastName : ""),
            a.StaffUserId, a.ExternalId,
            a.Status.ToString(), a.StartsAt, a.EndsAt,
            a.ServiceName, a.Notes, a.AtRiskAlertedAt, a.CreatedAt, a.UpdatedAt);

    /// <summary>
    /// Maps an appointment to DTO, loading the Customer if not already loaded.
    /// </summary>
    private async Task<AppointmentDto> ToDtoAsync(Appointment a, CancellationToken cancellationToken)
    {
        if (a.Customer is null)
        {
            a.Customer = await _db.Customers
                .AsNoTracking()
                .FirstAsync(c => c.Id == a.CustomerId, cancellationToken);
        }

        return ToDto(a);
    }
}
