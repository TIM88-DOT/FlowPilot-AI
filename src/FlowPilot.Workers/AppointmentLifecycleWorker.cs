using FlowPilot.Application.Appointments;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Workers;

/// <summary>
/// Background worker that polls appointments past their end time and transitions them:
/// - Confirmed → Completed (the customer attended)
/// - Scheduled → Missed (the customer never confirmed and didn't show up)
///
/// A grace period after EndsAt prevents premature transitions while staff is still wrapping up.
/// Configurable via Appointments:GracePeriodMinutes (default: 15 in prod, override to 1 in dev).
/// </summary>
public sealed class AppointmentLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppointmentLifecycleWorker> _logger;
    private readonly TimeSpan _gracePeriod;
    private readonly TimeSpan _atRiskWindow;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public AppointmentLifecycleWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<AppointmentLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        int graceMinutes = configuration.GetValue<int?>("Appointments:GracePeriodMinutes") ?? 15;
        _gracePeriod = TimeSpan.FromMinutes(graceMinutes);

        // Minutes override wins when set (useful for local test runs); otherwise fall back to hours (prod default 3h).
        int? atRiskMinutes = configuration.GetValue<int?>("Appointments:AtRiskWindowMinutes");
        if (atRiskMinutes.HasValue)
        {
            _atRiskWindow = TimeSpan.FromMinutes(atRiskMinutes.Value);
        }
        else
        {
            int atRiskHours = configuration.GetValue<int?>("Appointments:AtRiskWindowHours") ?? 3;
            _atRiskWindow = TimeSpan.FromHours(atRiskHours);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AppointmentLifecycleWorker started (grace period: {GraceMinutes} min, at-risk window: {AtRiskHours}h)",
            _gracePeriod.TotalMinutes, _atRiskWindow.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOverdueAppointmentsAsync(stoppingToken);
                await ProcessAtRiskAppointmentsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in AppointmentLifecycleWorker polling loop");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("AppointmentLifecycleWorker stopped");
    }

    private async Task ProcessOverdueAppointmentsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        DateTime cutoff = DateTime.UtcNow - _gracePeriod;

        // Fetch any active appointments past their end time + grace period — cross-tenant
        List<Appointment> overdue = await db.Appointments
            .IgnoreQueryFilters() // Worker operates across all tenants
            .Include(a => a.Customer)
            .Where(a => (a.Status == AppointmentStatus.Confirmed || a.Status == AppointmentStatus.Scheduled)
                        && a.EndsAt <= cutoff
                        && !a.IsDeleted)
            .OrderBy(a => a.EndsAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (overdue.Count == 0)
            return;

        _logger.LogInformation("Found {Count} appointments past their end time + grace period", overdue.Count);

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

                await db.SaveChangesAsync(cancellationToken);

                await mediator.Publish(new AppointmentStatusChangedEvent(
                    appointment.Id,
                    appointment.CustomerId,
                    appointment.TenantId,
                    Guid.Empty, // System-initiated, no user context
                    oldStatus,
                    newStatus), cancellationToken);

                if (newStatus == AppointmentStatus.Missed)
                {
                    await mediator.Publish(new AppointmentMissedEvent(
                        appointment.Id,
                        appointment.CustomerId,
                        appointment.TenantId,
                        appointment.StartsAt), cancellationToken);
                }

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
    }

    /// <summary>
    /// Scans for Scheduled (still unconfirmed) appointments entering the final confirmation
    /// window and flags them as at-risk. Publishes AppointmentAtRiskEvent so the dashboard
    /// lights up a red dot for staff. Idempotent via AtRiskAlertedAt.
    /// </summary>
    private async Task ProcessAtRiskAppointmentsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        DateTime now = DateTime.UtcNow;
        DateTime windowEnd = now + _atRiskWindow;

        List<Appointment> atRisk = await db.Appointments
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
            return;

        _logger.LogInformation("Found {Count} at-risk appointments entering final confirmation window", atRisk.Count);

        foreach (Appointment appointment in atRisk)
        {
            try
            {
                appointment.AtRiskAlertedAt = now;
                await db.SaveChangesAsync(cancellationToken);

                await mediator.Publish(new AppointmentAtRiskEvent(
                    appointment.Id,
                    appointment.CustomerId,
                    appointment.TenantId,
                    appointment.StartsAt), cancellationToken);

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
    }
}
