using System.Text.RegularExpressions;
using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Renders a template by finding the best locale variant and substituting variables.
/// Fallback chain: exact locale → tenant default language → "fr" (system fallback).
/// </summary>
public sealed partial class TemplateRenderer : ITemplateRenderer
{
    private readonly AppDbContext _db;

    public TemplateRenderer(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string?> RenderAsync(
        Guid templateId,
        string locale,
        Dictionary<string, string> variables,
        CancellationToken cancellationToken = default)
    {
        // Load all locale variants for this template
        List<TemplateLocaleVariant> variants = await _db.TemplateLocaleVariants
            .AsNoTracking()
            .Where(v => v.TemplateId == templateId)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
            return null;

        // Fallback chain: exact locale → "fr" (system default)
        TemplateLocaleVariant? variant =
            variants.FirstOrDefault(v => v.Locale.Equals(locale, StringComparison.OrdinalIgnoreCase))
            ?? variants.FirstOrDefault(v => v.Locale.Equals("fr", StringComparison.OrdinalIgnoreCase))
            ?? variants.First();

        // Substitute {{variable_name}} placeholders
        string body = variant.Body;
        foreach (KeyValuePair<string, string> kvp in variables)
        {
            body = body.Replace($"{{{{{kvp.Key}}}}}", kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        return body;
    }
}
