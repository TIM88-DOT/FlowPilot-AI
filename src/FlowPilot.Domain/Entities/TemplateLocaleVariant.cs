using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// A locale-specific variant of a template (e.g., fr, en).
/// Rendering chain: locale match → tenant default → system default.
/// </summary>
public class TemplateLocaleVariant : BaseEntity
{
    public Guid TemplateId { get; set; }
    public string Locale { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public Template Template { get; set; } = null!;
}
