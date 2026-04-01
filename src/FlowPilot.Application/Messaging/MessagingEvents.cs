using MediatR;

namespace FlowPilot.Application.Messaging;

/// <summary>
/// Published when a customer opts out via SMS (STOP keyword).
/// Handlers should cancel all pending scheduled messages for this customer.
/// </summary>
public sealed record CustomerOptedOutEvent(
    Guid CustomerId,
    Guid TenantId) : INotification;

/// <summary>
/// Published when a non-STOP inbound SMS is received and persisted.
/// Triggers ReplyHandlingAgent classification and real-time UI updates.
/// </summary>
public sealed record InboundSmsReceivedEvent(
    Guid MessageId,
    Guid CustomerId,
    Guid TenantId,
    string Body,
    string FromPhone) : INotification;
