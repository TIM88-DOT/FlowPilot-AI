using FlowPilot.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Workers;

/// <summary>
/// Background worker that polls for pending scheduled messages and dispatches them.
/// The dispatch logic (consent gate, appointment-no-longer-Scheduled cancellation, send +
/// usage tracking) lives in <see cref="IReminderDispatchService"/> (Infrastructure) so it is
/// testable independent of this polling loop. Each poll runs within its own scope.
/// </summary>
public sealed class ScheduledMessageDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledMessageDispatcher> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5); // TEST MODE — was 30s

    public ScheduledMessageDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledMessageDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledMessageDispatcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                IReminderDispatchService dispatch =
                    scope.ServiceProvider.GetRequiredService<IReminderDispatchService>();

                await dispatch.DispatchDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in ScheduledMessageDispatcher polling loop");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("ScheduledMessageDispatcher stopped");
    }
}
