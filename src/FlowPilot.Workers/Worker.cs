namespace FlowPilot.Workers;

/// <summary>
/// Placeholder — kept for backwards compatibility. Real dispatch is handled by ScheduledMessageDispatcher.
/// </summary>
public class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
