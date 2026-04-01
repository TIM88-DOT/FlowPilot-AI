using FlowPilot.Shared.Interfaces;

namespace FlowPilot.Api.Services;

/// <summary>
/// Resolves the current tenant from JWT claims in the HTTP context.
/// Falls back to empty GUIDs when no authenticated user is present (e.g., design-time, migrations).
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
                : Guid.Empty;

    public Guid UserId =>
        Guid.TryParse(_httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value, out Guid userId)
            ? userId
            : Guid.Empty;

    public string UserRole =>
        _httpContextAccessor.HttpContext?.User?.FindFirst("role")?.Value ?? string.Empty;
}
