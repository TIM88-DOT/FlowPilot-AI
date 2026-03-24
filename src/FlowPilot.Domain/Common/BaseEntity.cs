namespace FlowPilot.Domain.Common;

/// <summary>
/// Base class for all domain entities. Provides multi-tenancy, soft delete, and audit fields.
/// EF Core global query filters enforce TenantId isolation and soft delete exclusion.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
