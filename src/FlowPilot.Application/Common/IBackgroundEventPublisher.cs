using MediatR;

namespace FlowPilot.Application.Common;

/// <summary>
/// Publishes a domain event for out-of-band processing instead of running its handlers
/// synchronously on the calling (request) thread. Implementations capture the current tenant
/// context at enqueue time and replay it when the handlers run on a background scope, so a slow
/// or failing handler (e.g. an LLM agent or an outbound SMS) can never block or fail the request
/// that produced the event.
/// </summary>
public interface IBackgroundEventPublisher
{
    /// <summary>
    /// Enqueues a domain event to be published to its MediatR handlers on a background scope.
    /// Returns immediately; handler failures are isolated and logged, never propagated.
    /// </summary>
    void Enqueue(INotification domainEvent);
}
