using FlowPilot.Application.Appointments;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Workers;

/// <summary>
/// Background worker that polls for confirmed appointments past their end time
/// and transitions them to Completed, triggering downstream handlers (audit log, review requests).
/// </summary>
public sealed class AppointmentAutoCompletionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppointmentAutoCompletionWorker> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public AppointmentAutoCompletionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AppointmentAutoCompletionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AppointmentAutoCompletionWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CompleteOverdueAppointmentsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in AppointmentAutoCompletionWorker polling loop");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("AppointmentAutoCompletionWorker stopped");
    }

    private async Task CompleteOverdueAppointmentsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Fetch confirmed appointments whose end time has passed — cross-tenant
        List<Appointment> overdueAppointments = await db.Appointments
            .IgnoreQueryFilters() // Worker operates across all tenants
            .Where(a => a.Status == AppointmentStatus.Confirmed
                        && a.EndsAt <= DateTime.UtcNow
                        && !a.IsDeleted)
            .OrderBy(a => a.EndsAt)
            .Take(50) // Process in batches to avoid memory pressure
            .ToListAsync(cancellationToken);

        if (overdueAppointments.Count == 0)
            return;

        _logger.LogInformation("Found {Count} confirmed appointments past their end time", overdueAppointments.Count);

        foreach (Appointment appointment in overdueAppointments)
        {
            try
            {
                appointment.Status = AppointmentStatus.Completed;

                await db.SaveChangesAsync(cancellationToken);

                await mediator.Publish(new AppointmentStatusChangedEvent(
                    appointment.Id,
                    appointment.CustomerId,
                    appointment.TenantId,
                    Guid.Empty, // System-initiated, no user context
                    AppointmentStatus.Confirmed,
                    AppointmentStatus.Completed), cancellationToken);

                _logger.LogInformation(
                    "Auto-completed Appointment {AppointmentId} for Tenant {TenantId}",
                    appointment.Id, appointment.TenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to auto-complete Appointment {AppointmentId} for Tenant {TenantId}",
                    appointment.Id, appointment.TenantId);
            }
        }
    }
}
