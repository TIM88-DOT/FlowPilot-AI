using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Idempotency table for Service Bus consumers. EventId header checked before processing.
/// </summary>
public class ProcessedEvent : BaseEntity
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}
