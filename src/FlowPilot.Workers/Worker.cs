namespace FlowPilot.Workers;

/// <summary>
/// Placeholder — real workers (Service Bus consumers) will be added in Phase 2.
/// </summary>
public class Worker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
