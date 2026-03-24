using System.Text.Json;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Auth;

/// <summary>
/// Checks feature flags from the current tenant's Plan.FeatureFlags JSON column.
/// </summary>
public sealed class FeatureGateService : IFeatureGate
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;

    public FeatureGateService(AppDbContext db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string feature, CancellationToken cancellationToken = default)
    {
        string? featureFlags = await _db.Plans
            .AsNoTracking()
            .Where(p => p.TenantId == _currentTenant.TenantId)
            .Select(p => p.FeatureFlags)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(featureFlags))
            return false;

        using JsonDocument doc = JsonDocument.Parse(featureFlags);

        if (doc.RootElement.TryGetProperty(feature, out JsonElement value) && value.ValueKind == JsonValueKind.True)
            return true;

        return false;
    }
}
