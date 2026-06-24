using FlowPilot.Application.Appointments;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Appointments;

/// <summary>
/// Cross-tenant appointment lifecycle scans (overdue transitions + at-risk flagging).
/// Extracted from AppointmentLifecycleWorker so the logic is testable independent of the
/// hosted polling loop. The worker is now a thin scheduler that calls these methods.
/// </summary>
public sealed class AppointmentLifecycleService : IAppointmentLifecycleService
{
    private readonly AppDbContext _db;
    private readonly IMediator _mediator;
    private readonly ILogger<AppointmentLifecycleService> _logger;

    public AppointmentLifecycleService(
        AppDbContext db,
        IMediator mediator,
        ILogger<AppointmentLifecycleService> logger)
    {
        _db = db;
        _mediator = mediator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> ScanOverdueAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        DateTime cutoff = DateTime.UtcNow - gracePeriod;

        // Fetch any active appointments past their end time + grace period — cross-tenant
        List<Appointment> overdue = await _db.Appointments
            .IgnoreQueryFilters() // Worker operates across all tenants
            .Include(a => a.Customer)
            .Where(a => (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Scheduled)
                        && a.EndsAt <= cutoff
                        && !a.IsDeleted)
            .OrderBy(a => a.EndsAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0)
            return 0;

        _logger.LogInformation("Found {Count} appointments past their end time + grace period", overdue.Count);

        int transitioned = 0;

        foreach (Appointment appointment in overdue)
        {
            try
            {
                AppointmentStatus oldStatus = appointment.Status;
                AppointmentStatus newStatus = oldStatus == AppointmentStatus.Confirmed
                    ? AppointmentStatus.Completed   // Customer confirmed → assume they came
                    : AppointmentStatus.Missed;     // Customer never confirmed → no-show

                appointment.Status = newStatus;

                if (newStatus == AppointmentStatus.Missed)
                {
                    // Bump the no-show score (capped at 1.0)
                    appointment.Customer.NoShowScore = Math.Min(1.0m, appointment.Customer.NoShowScore + 0.1m);
                }

                await _db.SaveChangesAsync(cancellationToken);

                await _mediator.Publish(new AppointmentStatusChangedEvent(
                    appointment.Id,
                    appointment.CustomerId,
                    appointment.TenantId,
                    Guid.Empty, // System-initiated, no user context
                    oldStatus,
                    newStatus), cancellationToken);

                if (newStatus == AppointmentStatus.Missed)
                {
                    await _mediator.Publish(new AppointmentMissedEvent(
                        appointment.Id,
                        appointment.CustomerId,
                        appointment.TenantId,
                        appointment.StartsAt), cancellationToken);
                }

                transitioned++;

                _logger.LogInformation(
                    "Auto-transitioned Appointment {AppointmentId} for Tenant {TenantId}: {OldStatus} → {NewStatus}",
                    appointment.Id, appointment.TenantId, oldStatus, newStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to auto-transition Appointment {AppointmentId} for Tenant {TenantId}",
                    appointment.Id, appointment.TenantId);
            }
        }

        return transitioned;
    }

    /// <inheritdoc />
    public async Task<int> ScanAtRiskAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;
        DateTime windowEnd = now + window;

        List<Appointment> atRisk = await _db.Appointments
            .IgnoreQueryFilters() // Worker operates across all tenants
            .Where(a => a.Status == AppointmentStatus.Scheduled
                        && a.StartsAt > now
                        && a.StartsAt <= windowEnd
                        && a.AtRiskAlertedAt == null
                        && !a.IsDeleted)
            .OrderBy(a => a.StartsAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (atRisk.Count == 0)
            return 0;

        _logger.LogInformation("Found {Count} at-risk appointments entering final confirmation window", atRisk.Count);

        int flagged = 0;

        foreach (Appointment appointment in atRisk)
        {
            try
            {
                appointment.AtRiskAlertedAt = now;
                await _db.SaveChangesAsync(cancellationToken);

                await _mediator.Publish(new AppointmentAtRiskEvent(
                    appointment.Id,
                    appointment.CustomerId,
                    appointment.TenantId,
                    appointment.StartsAt), cancellationToken);

                flagged++;

                _logger.LogInformation(
                    "Flagged Appointment {AppointmentId} for Tenant {TenantId} as at-risk (StartsAt: {StartsAt})",
                    appointment.Id, appointment.TenantId, appointment.StartsAt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to flag Appointment {AppointmentId} for Tenant {TenantId} as at-risk",
                    appointment.Id, appointment.TenantId);
            }
        }

        return flagged;
    }
}
