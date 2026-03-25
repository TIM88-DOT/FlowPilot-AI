namespace FlowPilot.Application.Messaging;

/// <summary>
/// Renders a template with locale fallback and variable substitution.
/// Fallback chain: exact locale → tenant default language → "fr" (system default).
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// Finds the best locale variant for the given template and renders it with variables.
    /// Variables use {{variable_name}} syntax.
    /// </summary>
    Task<string?> RenderAsync(
        Guid templateId,
        string locale,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken = default);
}
