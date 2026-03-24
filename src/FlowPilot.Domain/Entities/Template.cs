using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// SMS template with locale variants. Supports system-level and tenant-level templates.
/// </summary>
public class Template : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool IsSystem { get; set; }

    public ICollection<TemplateLocaleVariant> LocaleVariants { get; set; } = new List<TemplateLocaleVariant>();
}
