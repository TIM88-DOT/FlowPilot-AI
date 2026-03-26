using System.Text.Json;
using FlowPilot.Application.Agents;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Agents.Tools;

/// <summary>
/// Fetches a customer's profile, recent messages, appointment history, and no-show score.
/// </summary>
public sealed class GetCustomerHistoryTool : IAgentTool
{
    private readonly AppDbContext _db;

    public GetCustomerHistoryTool(AppDbContext db) => _db = db;

    public string Name => "get_customer_history";

    public string Description =>
        "Fetches a customer's profile including preferred language, consent status, no-show score, " +
        "recent messages (last 10), and recent appointments (last 10).";

    public BinaryData InputSchema => BinaryData.FromString("""
        {
            "type": "object",
            "properties": {
                "customerId": { "type": "string", "format": "uuid", "description": "The customer's ID" }
            },
            "required": ["customerId"]
        }
        """);

    public async Task<string> ExecuteAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        using JsonDocument doc = JsonDocument.Parse(inputJson);
        Guid customerId = Guid.Parse(doc.RootElement.GetProperty("customerId").GetString()!);

        Customer? customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        if (customer is null)
            return JsonSerializer.Serialize(new { error = "Customer not found." });

        List<object> recentMessages = await _db.Messages
            .AsNoTracking()
            .Where(m => m.CustomerId == customerId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .Select(m => new
            {
                m.Direction,
                m.Status,
                m.Body,
                m.CreatedAt
            })
            .ToListAsync<object>(cancellationToken);

        List<object> recentAppointments = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.StartsAt)
            .Take(10)
            .Select(a => new
            {
                a.Id,
                a.Status,
                a.StartsAt,
                a.EndsAt,
                a.ServiceName
            })
            .ToListAsync<object>(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            customer = new
            {
                customer.Id,
                customer.FirstName,
                customer.LastName,
                customer.Phone,
                customer.PreferredLanguage,
                customer.ConsentStatus,
                customer.NoShowScore,
                customer.Tags
            },
            recentMessages,
            recentAppointments
        });
    }
}
