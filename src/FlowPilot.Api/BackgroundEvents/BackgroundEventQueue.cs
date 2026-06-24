using System.Threading.Channels;
using MediatR;

namespace FlowPilot.Api.BackgroundEvents;

/// <summary>
/// A domain event captured for background publishing, together with the tenant context that was
/// current when it was enqueued. The context is restored before the event's handlers run so
/// tenant-scoped queries behave exactly as they would on the originating request.
/// </summary>
public sealed record QueuedDomainEvent(
    INotification Event,
    Guid TenantId,
    Guid UserId,
    string UserRole);

/// <summary>
/// In-process, unbounded queue of <see cref="QueuedDomainEvent"/>s. Singleton: the request-scoped
/// publisher writes, the hosted processor reads.
/// </summary>
public interface IBackgroundEventQueue
{
    ValueTask EnqueueAsync(QueuedDomainEvent item, CancellationToken cancellationToken = default);
    ValueTask<QueuedDomainEvent> DequeueAsync(CancellationToken cancellationToken);
    bool TryWrite(QueuedDomainEvent item);
}

/// <inheritdoc />
public sealed class BackgroundEventQueue : IBackgroundEventQueue
{
    // Unbounded: domain-event volume is bounded by request throughput and handlers are short-lived.
    // Items are not durable — process loss drops pending events (acceptable until Service Bus, Workstream B).
    private readonly Channel<QueuedDomainEvent> _channel =
        Channel.CreateUnbounded<QueuedDomainEvent>(new UnboundedChannelOptions
        {
            // Multiple concurrent readers: BackgroundEventProcessor runs a small pool of consumers
            // so one slow LLM handler doesn't stall the whole queue.
            SingleReader = false,
            SingleWriter = false,
        });

    public ValueTask EnqueueAsync(QueuedDomainEvent item, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(item, cancellationToken);

    public bool TryWrite(QueuedDomainEvent item) => _channel.Writer.TryWrite(item);

    public ValueTask<QueuedDomainEvent> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
