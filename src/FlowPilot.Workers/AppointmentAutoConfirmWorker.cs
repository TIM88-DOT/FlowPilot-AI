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
/// Background worker that auto-confirms Scheduled appointments starting within 3 hours.
/// If a customer didn't reply to reminders, the system assumes they're coming.
/// This ensures the auto-completion worker (Confirmed → Completed) can pick them up after EndsAt.
/// </summary>
public sealed class AppointmentAutoConfirmWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AppointmentAutoConfirmWorker> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5); // TEST MODE — was 30s
    private static readonly TimeSpan AutoConfirmWindow = TimeSpan.FromMinutes(3); // TEST MODE — was 3 hours

    public AppointmentAutoConfirmWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AppointmentAutoConfirmWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AppointmentAutoConfirmWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AutoConfirmApproachingAppointmentsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in AppointmentAutoConfirmWorker polling loop");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("AppointmentAutoConfirmWorker stopped");
    }

    private async Task AutoConfirmApproachingAppointmentsAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        DateTime cutoff = DateTime.UtcNow.Add(AutoConfirmWindow);

        // Find Scheduled appointments starting within 3 hours that were never confirmed
        List<Appointment> unconfirmed = await db.Appointments
            .IgnoreQueryFilters() // Worker operates across all tenants
            .Where(a => a.Status == AppointmentStatus.Scheduled
                        && a.StartsAt <= cutoff
                        && !a.IsDeleted)
            .OrderBy(a => a.StartsAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (unconfirmed.Count == 0)
            return;

        _logger.LogInformation(
            "Found {Count} scheduled appointments starting within {Hours}h — auto-confirming",
            unconfirmed.Count, AutoConfirmWindow.TotalHours);

        foreach (Appointment appointment in unconfirmed)
        {
            try
            {
                appointment.Status = AppointmentStatus.Confirmed;

                await db.SaveChangesAsync(cancellationToken);

                await mediator.Publish(new AppointmentStatusChangedEvent(
                    appointment.Id,
                    appointment.CustomerId,
                    appointment.TenantId,
                    Guid.Empty, // System-initiated, no user context
                    AppointmentStatus.Scheduled,
                    AppointmentStatus.Confirmed), cancellationToken);

                _logger.LogInformation(
                    "Auto-confirmed Appointment {AppointmentId} for Tenant {TenantId} (no customer reply)",
                    appointment.Id, appointment.TenantId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to auto-confirm Appointment {AppointmentId} for Tenant {TenantId}",
                    appointment.Id, appointment.TenantId);
            }
        }
    }
}
