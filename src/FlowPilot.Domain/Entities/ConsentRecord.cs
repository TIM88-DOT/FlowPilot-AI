using FlowPilot.Domain.Common;
using FlowPilot.Domain.Enums;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Append-only consent audit log for a customer.
/// </summary>
public class ConsentRecord : BaseEntity
{
    public Guid CustomerId { get; set; }
    public ConsentStatus Status { get; set; }
    public ConsentSource Source { get; set; }
    public string? Notes { get; set; }

    public Customer Customer { get; set; } = null!;
}
