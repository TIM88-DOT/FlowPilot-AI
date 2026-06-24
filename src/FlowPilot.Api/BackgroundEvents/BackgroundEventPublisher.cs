using FlowPilot.Application.Common;
using FlowPilot.Shared.Interfaces;
using MediatR;

namespace FlowPilot.Api.BackgroundEvents;

/// <summary>
/// Request-scoped <see cref="IBackgroundEventPublisher"/> that snapshots the current tenant and
/// hands the event off to the singleton <see cref="IBackgroundEventQueue"/> for out-of-band
/// processing by <see cref="BackgroundEventProcessor"/>.
/// </summary>
public sealed class BackgroundEventPublisher : IBackgroundEventPublisher
{
    private readonly IBackgroundEventQueue _queue;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<BackgroundEventPublisher> _logger;

    public BackgroundEventPublisher(
        IBackgroundEventQueue queue,
        ICurrentTenant currentTenant,
        ILogger<BackgroundEventPublisher> logger)
    {
        _queue = queue;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public void Enqueue(INotification domainEvent)
    {
        var item = new QueuedDomainEvent(
            domainEvent,
            _currentTenant.TenantId,
            _currentTenant.UserId,
            _currentTenant.UserRole);

        // Unbounded channel — TryWrite only fails if the queue was completed (shutdown).
        if (!_queue.TryWrite(item))
        {
            _logger.LogWarning(
                "Background event queue rejected {EventType} (queue closed); event dropped.",
                domainEvent.GetType().Name);
        }
    }
}
