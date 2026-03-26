using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Checks which review platforms the tenant has configured (Google, Facebook, Trustpilot).
/// Returns the platform URLs/IDs so the agent can include the correct review link in the SMS.
/// </summary>
public sealed class GetReviewPlatformsTool : IAgentTool
{
    private readonly AppDbContext _db;

    public GetReviewPlatformsTool(AppDbContext db) => _db = db;

    public string Name => "get_review_platforms";

    public string Description =>
        "Returns the tenant's configured review platforms (Google Place ID, Facebook page URL, Trustpilot URL). " +
        "Use this to determine which review link to include in a review request SMS.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {},
            "required": []
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        TenantSettings? settings = await _db.TenantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
            return JsonSerializer.Serialize(new { error = "Tenant settings not found.", configured = false });

        bool hasAnyPlatform = !string.IsNullOrEmpty(settings.GooglePlaceId)
            || !string.IsNullOrEmpty(settings.FacebookPageUrl)
            || !string.IsNullOrEmpty(settings.TrustpilotUrl);

        return JsonSerializer.Serialize(new
        {
            configured = hasAnyPlatform,
            googlePlaceId = settings.GooglePlaceId,
            googleReviewUrl = !string.IsNullOrEmpty(settings.GooglePlaceId)
                ? $"https://search.google.com/local/writereview?placeid={settings.GooglePlaceId}"
                : null,
            facebookPageUrl = settings.FacebookPageUrl,
            trustpilotUrl = settings.TrustpilotUrl
        });
    }
}
