using FlowPilot.Domain.Common;
using FlowPilot.Domain.Enums;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Inbound or outbound SMS message. ProviderSmsSid is the idempotency key for inbound.
/// ProviderMessageId + Status is the idempotency key for delivery status.
/// </summary>
public class Message : BaseEntity
{
    public Guid CustomerId { get; set; }
    public MessageDirection Direction { get; set; }
    public MessageStatus Status { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? FromPhone { get; set; }
    public string? ToPhone { get; set; }
    public string? ProviderSmsSid { get; set; }
    public string? ProviderMessageId { get; set; }
    public int? SegmentCount { get; set; }

    public Customer Customer { get; set; } = null!;
}
