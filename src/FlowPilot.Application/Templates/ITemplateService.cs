using FlowPilot.Shared;

namespace FlowPilot.Application.Templates;

/// <summary>
/// Template CRUD and locale variant management.
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// Lists all templates for the current tenant.
    /// </summary>
    Task<Result<List<TemplateDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a template by id with its locale variants.
    /// </summary>
    Task<Result<TemplateDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new template with locale variants.
    /// </summary>
    Task<Result<TemplateDto>> CreateAsync(CreateTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a template's metadata. Cannot update system templates.
    /// </summary>
    Task<Result<TemplateDto>> UpdateAsync(Guid id, UpdateTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a template. Cannot delete system templates.
    /// </summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a locale variant on a template.
    /// </summary>
    Task<Result<TemplateDto>> UpsertLocaleVariantAsync(Guid templateId, UpsertLocaleVariantRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a locale variant from a template.
    /// </summary>
    Task<Result> DeleteLocaleVariantAsync(Guid templateId, string locale, CancellationToken cancellationToken = default);
}
