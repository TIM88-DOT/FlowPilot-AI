using MediatR;

namespace FlowPilot.Application.Messaging;

/// <summary>
/// Published when a customer opts out via SMS (STOP keyword).
/// Handlers should cancel all pending scheduled messages for this customer.
/// </summary>
public sealed record CustomerOptedOutEvent(
    Guid CustomerId,
    Guid TenantId) : INotification;
