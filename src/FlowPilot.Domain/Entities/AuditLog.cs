using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Audit trail entry for appointment status changes and other important events.
/// </summary>
public class AuditLog : BaseEntity
{
    public Guid? AppointmentId { get; set; }
    public Guid? UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public Appointment? Appointment { get; set; }
}
