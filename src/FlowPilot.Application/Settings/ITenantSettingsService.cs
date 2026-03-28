using FlowPilot.Shared;

namespace FlowPilot.Application.Settings;

/// <summary>
/// Reads and updates the combined Tenant + TenantSettings for the current tenant.
/// </summary>
public interface ITenantSettingsService
{
    /// <summary>
    /// Returns the full settings object for the current tenant.
    /// </summary>
    Task<Result<TenantSettingsDto>> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Partially updates tenant business info and/or settings. Only non-null fields are applied.
    /// </summary>
    Task<Result<TenantSettingsDto>> UpdateAsync(UpdateTenantSettingsRequest request, CancellationToken cancellationToken = default);
}
