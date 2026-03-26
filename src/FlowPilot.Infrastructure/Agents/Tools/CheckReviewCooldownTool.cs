using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Hard C# gate: checks whether a customer has received a review request in the last 30 days.
/// The AI cannot override this — if cooldown is active, no review SMS should be sent.
/// </summary>
public sealed class CheckReviewCooldownTool : IAgentTool
{
    private readonly AppDbContext _db;

    /// <summary>Minimum days between review requests for the same customer.</summary>
    private const int CooldownDays = 30;

    public CheckReviewCooldownTool(AppDbContext db) => _db = db;

    public string Name => "check_review_cooldown";

    public string Description =>
        "Checks if a customer is eligible for a review request. Returns false if a review SMS was sent " +
        "within the last 30 days. This is a hard business rule — you MUST NOT send a review request " +
        "if canSendReview is false.";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "customerId": { "type": "string", "format": "uuid", "description": "The customer to check" }
            },
            "required": ["customerId"]
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        Guid customerId = Guid.Parse(doc.RootElement.GetProperty("customerId").GetString()!);

        DateTime cutoff = DateTime.UtcNow.AddDays(-CooldownDays);

        // Check if any outbound message containing "review" was sent to this customer recently.
        // A more robust approach would use a dedicated ReviewRequest entity, but for MVP
        // we check the message log.
        bool recentReviewSent = await _db.Messages
            .AsNoTracking()
            .AnyAsync(m =>
                m.CustomerId == customerId
                && m.Direction == MessageDirection.Outbound
                && m.Status != MessageStatus.Failed
                && m.Body.Contains("review")
                && m.CreatedAt >= cutoff,
                cancellationToken);

        return JsonSerializer.Serialize(new
        {
            customerId,
            canSendReview = !recentReviewSent,
            cooldownDays = CooldownDays,
            reason = recentReviewSent
                ? $"A review request was sent within the last {CooldownDays} days."
                : "Customer is eligible for a review request."
        });
    }
}
