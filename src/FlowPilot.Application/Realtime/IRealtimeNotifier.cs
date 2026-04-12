namespace FlowPilot.Application.Realtime;

/// <summary>
/// Publishes realtime events to browser clients via the SignalR hub layer.
/// Implementations must work across processes — both the API (which hosts the hubs)
/// and the Workers (which can't reach the hubs in-process) need to publish events
/// that eventually reach the same connected clients.
///
/// The default implementation uses Postgres LISTEN/NOTIFY as a free, in-DB backplane:
/// any process publishes, a listener in the API relays to the hubs.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Fan out an event to every client in the given tenant's group on the given hub.
    /// </summary>
    /// <param name="tenantId">Tenant scope — clients not in this tenant never receive the event.</param>
    /// <param name="hub">Hub identifier: "appointments" or "sms".</param>
    /// <param name="eventName">SignalR client method name, e.g. "AppointmentStatusChanged".</param>
    /// <param name="payload">Arbitrary JSON-serializable payload delivered to the client.</param>
    Task PublishAsync(
        Guid tenantId,
        string hub,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default);
}
