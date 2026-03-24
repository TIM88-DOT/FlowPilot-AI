namespace FlowPilot.Shared.Interfaces;

/// <summary>
/// Provides the current tenant context resolved from JWT claims.
/// TenantId must NEVER be sourced from request bodies.
/// </summary>
public interface ICurrentTenant
{
    Guid TenantId { get; }
    Guid UserId { get; }
    string UserRole { get; }
}
