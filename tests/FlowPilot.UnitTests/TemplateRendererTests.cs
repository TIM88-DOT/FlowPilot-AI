using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Messaging;
using FlowPilot.Infrastructure.Persistence;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests template locale fallback chain and variable substitution.
/// </summary>
public sealed class TemplateRendererTests : IDisposable
{
    private readonly TestDbFixture _fixture = new();
    private readonly TemplateRenderer _sut;
    private readonly AppDbContext _db;

    public TemplateRendererTests()
    {
        _db = _fixture.CreateContext();
        _sut = new TemplateRenderer(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _fixture.Dispose();
    }

    private async Task<Guid> SeedTemplateWithVariantsAsync(params (string locale, string body)[] variants)
    {
        var template = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            Name = "test-template",
            Category = "reminder"
        };
        _db.Templates.Add(template);

        foreach ((string locale, string body) in variants)
        {
            _db.TemplateLocaleVariants.Add(new TemplateLocaleVariant
            {
                Id = Guid.NewGuid(),
                TenantId = _fixture.TenantId,
                TemplateId = template.Id,
                Locale = locale,
                Body = body
            });
        }

        await _db.SaveChangesAsync();
        return template.Id;
    }

    // -----------------------------------------------------------------------
    // Locale fallback chain
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Render_ExactLocaleMatch_ReturnsMatched()
    {
        Guid templateId = await SeedTemplateWithVariantsAsync(
            ("fr", "Bonjour {{name}}"),
            ("ar", "مرحبا {{name}}"));

        string? result = await _sut.RenderAsync(templateId, "ar", new Dictionary<string, string> { ["name"] = "Ali" });

        Assert.NotNull(result);
        Assert.Equal("مرحبا Ali", result);
    }

    [Fact]
    public async Task Render_NoExactMatch_FallsBackToFr()
    {
        Guid templateId = await SeedTemplateWithVariantsAsync(
            ("fr", "Bonjour {{name}}"),
            ("ar", "مرحبا {{name}}"));

        string? result = await _sut.RenderAsync(templateId, "en", new Dictionary<string, string> { ["name"] = "John" });

        Assert.NotNull(result);
        Assert.Equal("Bonjour John", result);
    }

    [Fact]
    public async Task Render_NoExactAndNoFr_FallsBackToFirst()
    {
        Guid templateId = await SeedTemplateWithVariantsAsync(
            ("ar", "مرحبا {{name}}"),
            ("es", "Hola {{name}}"));

        string? result = await _sut.RenderAsync(templateId, "en", new Dictionary<string, string> { ["name"] = "Test" });

        Assert.NotNull(result);
        // Falls back to first variant (ar)
        Assert.Equal("مرحبا Test", result);
    }

    [Fact]
    public async Task Render_CaseInsensitiveLocaleMatch()
    {
        Guid templateId = await SeedTemplateWithVariantsAsync(
            ("FR", "Bonjour {{name}}"));

        string? result = await _sut.RenderAsync(templateId, "fr", new Dictionary<string, string> { ["name"] = "Test" });

        Assert.NotNull(result);
        Assert.Equal("Bonjour Test", result);
    }

    // -----------------------------------------------------------------------
    // Variable substitution
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Render_SubstitutesMultipleVariables()
    {
        Guid templateId = await SeedTemplateWithVariantsAsync(
            ("fr", "Bonjour {{name}}, votre RDV est le {{date}} a {{time}}."));

        string? result = await _sut.RenderAsync(templateId, "fr", new Dictionary<string, string>
        {
            ["name"] = "Ali",
            ["date"] = "28/03/2026",
            ["time"] = "14h00"
        });

        Assert.NotNull(result);
        Assert.Equal("Bonjour Ali, votre RDV est le 28/03/2026 a 14h00.", result);
    }

    [Fact]
    public async Task Render_CaseInsensitiveVariableReplacement()
    {
        Guid templateId = await SeedTemplateWithVariantsAsync(
            ("fr", "Bonjour {{NAME}}, bienvenue!"));

        string? result = await _sut.RenderAsync(templateId, "fr", new Dictionary<string, string> { ["name"] = "Ali" });

        Assert.NotNull(result);
        Assert.Equal("Bonjour Ali, bienvenue!", result);
    }

    [Fact]
    public async Task Render_UnmatchedVariable_RemainsInOutput()
    {
        Guid templateId = await SeedTemplateWithVariantsAsync(
            ("fr", "Hello {{name}}, your code is {{code}}."));

        // Only provide "name", not "code"
        string? result = await _sut.RenderAsync(templateId, "fr", new Dictionary<string, string> { ["name"] = "Ali" });

        Assert.NotNull(result);
        Assert.Contains("{{code}}", result);
        Assert.Contains("Ali", result);
    }

    // -----------------------------------------------------------------------
    // No variants
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Render_NoVariants_ReturnsNull()
    {
        // Template exists but has no locale variants
        var template = new Template
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            Name = "empty-template",
            Category = "test"
        };
        _db.Templates.Add(template);
        await _db.SaveChangesAsync();

        string? result = await _sut.RenderAsync(template.Id, "fr", new Dictionary<string, string>());

        Assert.Null(result);
    }

    [Fact]
    public async Task Render_NonexistentTemplate_ReturnsNull()
    {
        string? result = await _sut.RenderAsync(Guid.NewGuid(), "fr", new Dictionary<string, string>());

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    // Empty variables
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Render_EmptyVariables_ReturnsBodyUnchanged()
    {
        Guid templateId = await SeedTemplateWithVariantsAsync(
            ("fr", "Static text with no placeholders."));

        string? result = await _sut.RenderAsync(templateId, "fr", new Dictionary<string, string>());

        Assert.NotNull(result);
        Assert.Equal("Static text with no placeholders.", result);
    }
}
