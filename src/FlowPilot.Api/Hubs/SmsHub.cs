using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FlowPilot.Api.Hubs;

/// <summary>
/// SignalR hub for real-time SMS/inbox updates. Clients are grouped by tenant.
/// </summary>
[Authorize]
public sealed class SmsHub : Hub
{
    private readonly ILogger<SmsHub> _logger;

    public SmsHub(ILogger<SmsHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        string? tenantId = Context.User?.FindFirst("tenant_id")?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant-{tenantId}");
            _logger.LogInformation(
                "SmsHub client {ConnectionId} joined tenant group {TenantId}",
                Context.ConnectionId, tenantId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string? tenantId = Context.User?.FindFirst("tenant_id")?.Value;

        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant-{tenantId}");
        }

        await base.OnDisconnectedAsync(exception);
    }
}
