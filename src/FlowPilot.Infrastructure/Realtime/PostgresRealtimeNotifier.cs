using System.Text.Json;
using FlowPilot.Application.Realtime;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Realtime;

/// <summary>
/// Publishes realtime events via Postgres LISTEN/NOTIFY on a single well-known channel.
/// Both API and Workers use this implementation — the API also hosts the listener that
/// receives these notifications and bridges them to the SignalR hubs.
///
/// The payload is a compact JSON envelope under the pg_notify 8000-byte limit.
/// </summary>
public sealed class PostgresRealtimeNotifier : IRealtimeNotifier
{
    /// <summary>
    /// Single shared Postgres channel name. Kept in one place so the listener stays in sync.
    /// </summary>
    public const string Channel = "flowpilot_realtime";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _db;
    private readonly ILogger<PostgresRealtimeNotifier> _logger;

    public PostgresRealtimeNotifier(AppDbContext db, ILogger<PostgresRealtimeNotifier> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync(
        Guid tenantId,
        string hub,
        string eventName,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var envelope = new RealtimeEnvelope(tenantId, hub, eventName, payload);
        string json = JsonSerializer.Serialize(envelope, JsonOptions);

        if (json.Length > 7500)
        {
            // pg_notify hard-caps at 8000 bytes. We bail loudly rather than silently truncating.
            _logger.LogError(
                "Realtime envelope too large ({Size} bytes) — dropping event {Event} for tenant {TenantId}",
                json.Length, eventName, tenantId);
            return;
        }

        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                "SELECT pg_notify({0}, {1})",
                new object[] { Channel, json },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Realtime is best-effort. A DB hiccup must never break the originating business flow.
            _logger.LogError(ex,
                "Failed to publish realtime event {Event} to tenant {TenantId} on hub {Hub}",
                eventName, tenantId, hub);
        }
    }

    /// <summary>
    /// JSON envelope wrapped around every notification. The listener in the API deserializes
    /// this, looks up the matching hub, and forwards to the tenant's SignalR group.
    /// </summary>
    public sealed record RealtimeEnvelope(
        Guid TenantId,
        string Hub,
        string Event,
        object Payload);
}
