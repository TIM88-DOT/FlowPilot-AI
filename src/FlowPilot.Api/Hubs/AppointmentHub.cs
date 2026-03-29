using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FlowPilot.Api.Hubs;

/// <summary>
/// SignalR hub for real-time appointment updates. Clients are grouped by tenant
/// so events are scoped to the correct organization.
/// </summary>
[Authorize]
public sealed class AppointmentHub : Hub
{
    private readonly ILogger<AppointmentHub> _logger;

    public AppointmentHub(ILogger<AppointmentHub> logger)
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
                "SignalR client {ConnectionId} joined tenant group {TenantId}",
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
