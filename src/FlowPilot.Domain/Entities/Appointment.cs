using FlowPilot.Domain.Common;
using FlowPilot.Domain.Enums;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Appointment with status transition enforcement.
/// ExternalId + TenantId unique constraint for webhook idempotency.
/// </summary>
public class Appointment : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Guid? StaffUserId { get; set; }
    public string? ExternalId { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string? ServiceName { get; set; }
    public string? Notes { get; set; }

    public Customer Customer { get; set; } = null!;
    public User? StaffUser { get; set; }
    public ICollection<ScheduledMessage> ScheduledMessages { get; set; } = new List<ScheduledMessage>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
