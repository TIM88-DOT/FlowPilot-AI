using FlowPilot.Shared.Interfaces;

namespace FlowPilot.Workers;

/// <summary>
/// No-op ICurrentTenant for background workers. Workers operate across all tenants
/// and use IgnoreQueryFilters() for cross-tenant queries. This implementation returns
/// Guid.Empty to satisfy the DI requirement — queries must use IgnoreQueryFilters().
/// </summary>
public sealed class WorkerTenant : ICurrentTenant
{
    public Guid TenantId => Guid.Empty;
    public Guid UserId => Guid.Empty;
    public string UserRole => "System";
}
