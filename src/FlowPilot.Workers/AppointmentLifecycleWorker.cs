using FlowPilot.Application.Appointments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Workers;

/// <summary>
/// Background worker that polls appointments and drives lifecycle transitions:
/// - Confirmed → Completed (the customer attended)
/// - Scheduled → Missed (the customer never confirmed and didn't show up)
/// - Scheduled → at-risk flag when entering the final confirmation window
///
/// The actual scan logic lives in <see cref="IAppointmentLifecycleService"/> (Infrastructure)
/// so it is testable independent of this polling loop. This worker only schedules the scans.
///
/// A grace period after EndsAt prevents premature transitions while staff is still wrapping up.
/// Configurable via Appointments:GracePeriodMinutes (default: 15 in prod, override to 1 in dev).
/// The at-risk window is configurable via Appointments:AtRiskWindowHours (default 3h), with an
/// optional Appointments:AtRiskWindowMinutes override for fast local test runs.
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
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAppointmentLifecycleService lifecycle =
                    scope.ServiceProvider.GetRequiredService<IAppointmentLifecycleService>();

                await lifecycle.ScanOverdueAsync(_gracePeriod, stoppingToken);
                await lifecycle.ScanAtRiskAsync(_atRiskWindow, stoppingToken);
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
}
