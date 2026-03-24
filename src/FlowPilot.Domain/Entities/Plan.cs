using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Subscription plan for a tenant. FeatureFlags drives IFeatureGate checks.
/// </summary>
public class Plan : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int MaxSmsPerMonth { get; set; }
    public int MaxCustomers { get; set; }
    public int MaxAgentRunsPerMonth { get; set; }

    /// <summary>
    /// JSON object of feature flags, e.g. {"reviewRecovery": true, "campaigns": false}
    /// </summary>
    public string FeatureFlags { get; set; } = "{}";

    public Tenant Tenant { get; set; } = null!;
    public ICollection<UsageRecord> UsageRecords { get; set; } = new List<UsageRecord>();
}
