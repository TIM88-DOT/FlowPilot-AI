using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Represents a business tenant in the multi-tenant system.
/// </summary>
public class Tenant : BaseEntity
{
    public string BusinessName { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? BusinessPhone { get; set; }
    public string? BusinessEmail { get; set; }
    public string? Timezone { get; set; }
    public string DefaultLanguage { get; set; } = "fr";
    public string? Address { get; set; }
    public string Currency { get; set; } = "CAD";

    public TenantSettings? Settings { get; set; }
    public Plan? Plan { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
}
