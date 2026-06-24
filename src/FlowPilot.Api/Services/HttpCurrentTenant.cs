using FlowPilot.Shared.Interfaces;

namespace FlowPilot.Api.Services;

/// <summary>
/// Resolves the current tenant from JWT claims in the HTTP context.
/// When no HTTP request is in scope (e.g. a background event replay), falls back to the
/// <see cref="AmbientTenant"/> override. Falls back to empty GUIDs otherwise (design-time, migrations).
/// </summary>
public class HttpCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentTenant(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid TenantId =>
        Guid.TryParse(_httpContextAccessor.HttpContext?.User?.FindFirst("tenant_id")?.Value, out Guid tenantId)
            ? tenantId
            : _httpContextAccessor.HttpContext?.Items["PublicTenantId"] is Guid publicTenantId
                ? publicTenantId
                : AmbientTenant.Current?.TenantId ?? Guid.Empty;

    public Guid UserId =>
        Guid.TryParse(_httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value, out Guid userId)
            ? userId
            : AmbientTenant.Current?.UserId ?? Guid.Empty;

    public string UserRole =>
        _httpContextAccessor.HttpContext?.User?.FindFirst("role")?.Value
            ?? AmbientTenant.Current?.UserRole
            ?? string.Empty;
}
