using System.Text.Json;
using FlowPilot.Infrastructure.Realtime;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

namespace FlowPilot.Api.Hubs;

/// <summary>
/// Holds a dedicated long-lived Npgsql connection that LISTENs on the realtime channel.
/// Every notification is deserialized into a <see cref="PostgresRealtimeNotifier.RealtimeEnvelope"/>
/// and forwarded to the matching SignalR hub group scoped by tenant.
///
/// This is the bridge that lets Workers publish realtime events without reaching into
/// the API's in-process hub context: Workers call pg_notify, this listener relays it.
/// It auto-reconnects on failure with a fixed backoff.
/// </summary>
public sealed class PostgresRealtimeListener : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _connectionString;
    private readonly IHubContext<AppointmentHub> _appointmentHub;
    private readonly IHubContext<SmsHub> _smsHub;
    private readonly ILogger<PostgresRealtimeListener> _logger;

    public PostgresRealtimeListener(
        IConfiguration configuration,
        IHubContext<AppointmentHub> appointmentHub,
        IHubContext<SmsHub> smsHub,
        ILogger<PostgresRealtimeListener> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured.");
        _appointmentHub = appointmentHub;
        _smsHub = smsHub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PostgresRealtimeListener starting, channel: {Channel}",
            PostgresRealtimeNotifier.Channel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(stoppingToken);

                conn.Notification += OnNotification;

                await using (var cmd = new NpgsqlCommand(
                    $"LISTEN {PostgresRealtimeNotifier.Channel};", conn))
                {
                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }

                _logger.LogInformation(
                    "PostgresRealtimeListener subscribed to {Channel}",
                    PostgresRealtimeNotifier.Channel);

                // WaitAsync blocks until a notification arrives and fires the event handler.
                // We loop so we can keep receiving notifications until shutdown.
                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "PostgresRealtimeListener error, reconnecting in {Delay}s",
                    ReconnectDelay.TotalSeconds);

                try
                {
                    await Task.Delay(ReconnectDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("PostgresRealtimeListener stopped");
    }

    /// <summary>
    /// Notification callback — fire-and-forget dispatch to the matching hub group.
    /// This handler is synchronous; SendAsync runs on the thread pool and we log any failures.
    /// </summary>
    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(e.Payload, JsonOptions);
            if (envelope is null)
            {
                _logger.LogWarning("Received empty/invalid realtime envelope");
                return;
            }

            string group = $"tenant-{envelope.TenantId}";

            _logger.LogInformation(
                "Relaying realtime event {Event} to hub {Hub} group {Group}",
                envelope.Event, envelope.Hub, group);

            Task send = envelope.Hub switch
            {
                "appointments" => _appointmentHub.Clients.Group(group).SendAsync(envelope.Event, envelope.Payload),
                "sms" => _smsHub.Clients.Group(group).SendAsync(envelope.Event, envelope.Payload),
                _ => HandleUnknownHub(envelope.Hub)
            };

            // Observe the task so unhandled exceptions don't get silently dropped.
            _ = send.ContinueWith(
                t => _logger.LogError(t.Exception, "Failed to push realtime event {Event} to group {Group}",
                    envelope.Event, group),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process realtime notification payload: {Payload}", e.Payload);
        }
    }

    private Task HandleUnknownHub(string hub)
    {
        _logger.LogWarning("Received realtime event for unknown hub: {Hub}", hub);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Mirror of <see cref="PostgresRealtimeNotifier.RealtimeEnvelope"/> with Payload as a raw
    /// JsonElement so SignalR can pass it through to the client unchanged.
    /// </summary>
    private sealed record Envelope(Guid TenantId, string Hub, string Event, JsonElement Payload);
}
