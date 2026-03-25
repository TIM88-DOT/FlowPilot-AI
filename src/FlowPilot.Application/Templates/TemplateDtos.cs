namespace FlowPilot.Application.Templates;

/// <summary>
/// Request to create a new template.
/// </summary>
public sealed record CreateTemplateRequest(
    string Name,
    string? Description,
    string Category,
    List<CreateLocaleVariantRequest> LocaleVariants);

/// <summary>
/// Request to create or update a locale variant.
/// </summary>
public sealed record CreateLocaleVariantRequest(
    string Locale,
    string Body);

/// <summary>
/// Request to update a template.
/// </summary>
public sealed record UpdateTemplateRequest(
    string? Name = null,
    string? Description = null,
    string? Category = null);

/// <summary>
/// Request to upsert a locale variant on an existing template.
/// </summary>
public sealed record UpsertLocaleVariantRequest(
    string Locale,
    string Body);

/// <summary>
/// Template data returned to callers.
/// </summary>
public sealed record TemplateDto(
    Guid Id,
    string Name,
    string? Description,
    string Category,
    bool IsSystem,
    List<LocaleVariantDto> LocaleVariants,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Locale variant data.
/// </summary>
public sealed record LocaleVariantDto(
    Guid Id,
    string Locale,
    string Body,
    int SegmentCount);
