using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// When a customer opts out, cancel all their pending scheduled messages.
/// </summary>
public sealed class CustomerOptedOutHandler : INotificationHandler<CustomerOptedOutEvent>
{
    private readonly AppDbContext _db;
    private readonly ILogger<CustomerOptedOutHandler> _logger;

    public CustomerOptedOutHandler(AppDbContext db, ILogger<CustomerOptedOutHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Handle(CustomerOptedOutEvent notification, CancellationToken cancellationToken)
    {
        var pendingMessages = await _db.ScheduledMessages
            .Where(sm => sm.CustomerId == notification.CustomerId && sm.Status == ScheduledMessageStatus.Pending)
            .ToListAsync(cancellationToken);

        if (pendingMessages.Count == 0)
            return;

        foreach (var message in pendingMessages)
        {
            message.Status = ScheduledMessageStatus.Cancelled;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Cancelled {Count} pending scheduled messages for opted-out customer {CustomerId}",
            pendingMessages.Count, notification.CustomerId);
    }
}
