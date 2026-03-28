using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// A service offered by the tenant (e.g. "Haircut", "Consultation").
/// Used in appointment creation and booking forms.
/// </summary>
public class Service : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = 30;
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
