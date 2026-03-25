using FlowPilot.Application.Templates;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Templates;

/// <summary>
/// Template CRUD and locale variant management.
/// </summary>
public sealed class TemplateService : ITemplateService
{
    private readonly AppDbContext _db;

    public TemplateService(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<Result<List<TemplateDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<TemplateDto> templates = await _db.Templates
            .AsNoTracking()
            .Include(t => t.LocaleVariants)
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .Select(t => ToDto(t))
            .ToListAsync(cancellationToken);

        return Result.Success(templates);
    }

    /// <inheritdoc />
    public async Task<Result<TemplateDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Template? template = await _db.Templates
            .AsNoTracking()
            .Include(t => t.LocaleVariants)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
            return Result.Failure<TemplateDto>(Error.NotFound("Template", id));

        return Result.Success(ToDto(template));
    }

    /// <inheritdoc />
    public async Task<Result<TemplateDto>> CreateAsync(CreateTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var templateId = Guid.NewGuid();
        var template = new Template
        {
            Id = templateId,
            Name = request.Name,
            Description = request.Description,
            Category = request.Category,
            IsSystem = false,
            LocaleVariants = request.LocaleVariants.Select(v => new TemplateLocaleVariant
            {
                Id = Guid.NewGuid(),
                TemplateId = templateId,
                Locale = v.Locale,
                Body = v.Body
            }).ToList()
        };

        _db.Templates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(template));
    }

    /// <inheritdoc />
    public async Task<Result<TemplateDto>> UpdateAsync(Guid id, UpdateTemplateRequest request, CancellationToken cancellationToken = default)
    {
        Template? template = await _db.Templates
            .Include(t => t.LocaleVariants)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
            return Result.Failure<TemplateDto>(Error.NotFound("Template", id));

        if (template.IsSystem)
            return Result.Failure<TemplateDto>(Error.Validation("Template.SystemReadOnly", "Cannot modify system templates."));

        if (request.Name is not null) template.Name = request.Name;
        if (request.Description is not null) template.Description = request.Description;
        if (request.Category is not null) template.Category = request.Category;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(template));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Template? template = await _db.Templates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (template is null)
            return Result.Failure(Error.NotFound("Template", id));

        if (template.IsSystem)
            return Result.Failure(Error.Validation("Template.SystemReadOnly", "Cannot delete system templates."));

        template.IsDeleted = true;
        template.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<TemplateDto>> UpsertLocaleVariantAsync(Guid templateId, UpsertLocaleVariantRequest request, CancellationToken cancellationToken = default)
    {
        Template? template = await _db.Templates
            .Include(t => t.LocaleVariants)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template is null)
            return Result.Failure<TemplateDto>(Error.NotFound("Template", templateId));

        TemplateLocaleVariant? existing = template.LocaleVariants
            .FirstOrDefault(v => v.Locale.Equals(request.Locale, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Body = request.Body;
        }
        else
        {
            var variant = new TemplateLocaleVariant
            {
                Id = Guid.NewGuid(),
                TemplateId = templateId,
                Locale = request.Locale,
                Body = request.Body
            };
            _db.TemplateLocaleVariants.Add(variant);
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Reload to get fresh locale variants
        await _db.Entry(template).Collection(t => t.LocaleVariants).LoadAsync(cancellationToken);

        return Result.Success(ToDto(template));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteLocaleVariantAsync(Guid templateId, string locale, CancellationToken cancellationToken = default)
    {
        Template? template = await _db.Templates
            .Include(t => t.LocaleVariants)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);

        if (template is null)
            return Result.Failure(Error.NotFound("Template", templateId));

        TemplateLocaleVariant? variant = template.LocaleVariants
            .FirstOrDefault(v => v.Locale.Equals(locale, StringComparison.OrdinalIgnoreCase));

        if (variant is null)
            return Result.Failure(Error.NotFound("LocaleVariant", templateId));

        _db.TemplateLocaleVariants.Remove(variant);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    // -----------------------------------------------------------------------
    // Mapping
    // -----------------------------------------------------------------------

    private static TemplateDto ToDto(Template t) =>
        new(t.Id, t.Name, t.Description, t.Category, t.IsSystem,
            t.LocaleVariants.Select(v => new LocaleVariantDto(
                v.Id, v.Locale, v.Body, EstimateSegments(v.Body))).ToList(),
            t.CreatedAt, t.UpdatedAt);

    /// <summary>
    /// Estimates SMS segment count. GSM-7: 160 chars/segment. UCS-2: 70 chars/segment.
    /// Multi-part: 153 / 67 chars per segment.
    /// </summary>
    private static int EstimateSegments(string body)
    {
        int length = body.Length;
        if (length <= 160) return 1;
        return (int)Math.Ceiling(length / 153.0);
    }
}
