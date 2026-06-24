using FlowPilot.Api.Services;
using MediatR;

namespace FlowPilot.Api.BackgroundEvents;

/// <summary>
/// Drains the <see cref="IBackgroundEventQueue"/> and publishes each captured domain event to its
/// MediatR handlers on a fresh DI scope, with the originating tenant restored via
/// <see cref="AmbientTenant"/>. Each item runs in isolation: a handler that throws (an LLM 429, an
/// outbound SMS failure, a constraint violation) is logged and skipped — it can never affect the
/// request that produced the event, nor a sibling event.
/// </summary>
public sealed class BackgroundEventProcessor : BackgroundService
{
    private readonly IBackgroundEventQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundEventProcessor> _logger;

    public BackgroundEventProcessor(
        IBackgroundEventQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<BackgroundEventProcessor> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            QueuedDomainEvent item;
            try
            {
                item = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // shutting down
            }

            await ProcessAsync(item, stoppingToken);
        }
    }

    private async Task ProcessAsync(QueuedDomainEvent item, CancellationToken cancellationToken)
    {
        // Restore tenant context for the duration of this event's handlers, then always clear it
        // so the AsyncLocal never leaks onto the next iteration.
        AmbientTenant.Current = new AmbientTenant.TenantContext(item.TenantId, item.UserId, item.UserRole);
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IPublisher publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            await publisher.Publish(item.Event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Background handler for {EventType} (tenant {TenantId}) failed; event dropped.",
                item.Event.GetType().Name, item.TenantId);
        }
        finally
        {
            AmbientTenant.Current = null;
        }
    }
}
