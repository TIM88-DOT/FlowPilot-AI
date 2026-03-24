namespace FlowPilot.Shared.Interfaces;

/// <summary>
/// Checks feature availability for the current tenant based on Plan.FeatureFlags.
/// </summary>
public interface IFeatureGate
{
    /// <summary>
    /// Returns true if the given feature is enabled for the current tenant's plan.
    /// </summary>
    Task<bool> IsEnabledAsync(string feature, CancellationToken cancellationToken = default);
}
