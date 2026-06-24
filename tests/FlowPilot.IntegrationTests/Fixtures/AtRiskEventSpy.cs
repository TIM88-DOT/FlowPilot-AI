using System.Collections.Concurrent;
using FlowPilot.Application.Appointments;
using MediatR;

namespace FlowPilot.IntegrationTests.Fixtures;

/// <summary>
/// Test-only sink that records every AppointmentAtRiskEvent published through MediatR,
/// so tests can assert the at-risk event fires exactly once per appointment (idempotency).
/// Registered as a singleton alongside <see cref="SpyAtRiskHandler"/> in FlowPilotApiFactory.
/// </summary>
public sealed class AtRiskEventSpy
{
    private readonly ConcurrentQueue<Guid> _flagged = new();

    public void Record(Guid appointmentId) => _flagged.Enqueue(appointmentId);

    /// <summary>Number of times the at-risk event fired for a specific appointment.</summary>
    public int CountFor(Guid appointmentId) => _flagged.Count(id => id == appointmentId);
}

/// <summary>
/// Extra MediatR handler (added on top of the real AppointmentRealtimeBridge) that records
/// at-risk events into <see cref="AtRiskEventSpy"/> for assertions.
/// </summary>
public sealed class SpyAtRiskHandler : INotificationHandler<AppointmentAtRiskEvent>
{
    private readonly AtRiskEventSpy _spy;

    public SpyAtRiskHandler(AtRiskEventSpy spy) => _spy = spy;

    public Task Handle(AppointmentAtRiskEvent notification, CancellationToken cancellationToken)
    {
        _spy.Record(notification.AppointmentId);
        return Task.CompletedTask;
    }
}
