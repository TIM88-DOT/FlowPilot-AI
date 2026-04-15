using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Application.Messaging;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Workers;

/// <summary>
/// Background worker that polls for pending scheduled messages and dispatches them via ISmsProvider.
/// Runs on a 30-second interval. Each message is sent within its own scope to isolate failures.
/// </summary>
public sealed class ScheduledMessageDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledMessageDispatcher> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5); // TEST MODE — was 30s

    public ScheduledMessageDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledMessageDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledMessageDispatcher started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchDueMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in ScheduledMessageDispatcher polling loop");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }

        _logger.LogInformation("ScheduledMessageDispatcher stopped");
    }

    private async Task DispatchDueMessagesAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Fetch due messages across all tenants — ignore query filters for cross-tenant dispatch
        List<ScheduledMessage> dueMessages = await db.ScheduledMessages
            .IgnoreQueryFilters() // Worker operates across all tenants
            .Include(sm => sm.Customer)
            .Include(sm => sm.Appointment)
            .Where(sm => sm.Status == ScheduledMessageStatus.Pending
                         && sm.ScheduledAt <= DateTime.UtcNow
                         && !sm.IsDeleted)
            .OrderBy(sm => sm.ScheduledAt)
            .Take(50) // Process in batches to avoid memory pressure
            .ToListAsync(cancellationToken);

        if (dueMessages.Count == 0)
            return;

        _logger.LogInformation("Found {Count} due scheduled messages to dispatch", dueMessages.Count);

        foreach (ScheduledMessage message in dueMessages)
        {
            try
            {
                await DispatchSingleMessageAsync(scope.ServiceProvider, db, message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to dispatch ScheduledMessage {MessageId} for Tenant {TenantId}",
                    message.Id, message.TenantId);

                message.Status = ScheduledMessageStatus.Failed;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchSingleMessageAsync(
        IServiceProvider serviceProvider,
        AppDbContext db,
        ScheduledMessage scheduledMessage,
        CancellationToken cancellationToken)
    {
        // Verify customer is still opted-in
        if (scheduledMessage.Customer.ConsentStatus != ConsentStatus.OptedIn)
        {
            _logger.LogInformation(
                "Skipping ScheduledMessage {MessageId} — customer {CustomerId} consent is {Status}",
                scheduledMessage.Id, scheduledMessage.CustomerId, scheduledMessage.Customer.ConsentStatus);

            scheduledMessage.Status = ScheduledMessageStatus.Cancelled;
            return;
        }

        // Skip if the appointment is no longer in Scheduled state — the customer already
        // confirmed, rescheduled, cancelled, or the appointment was marked missed/completed.
        // Prevents firing a second reminder when it's no longer relevant.
        if (scheduledMessage.Appointment.Status != AppointmentStatus.Scheduled)
        {
            _logger.LogInformation(
                "Skipping ScheduledMessage {MessageId} — appointment {AppointmentId} is {Status}",
                scheduledMessage.Id, scheduledMessage.AppointmentId, scheduledMessage.Appointment.Status);

            scheduledMessage.Status = ScheduledMessageStatus.Cancelled;
            return;
        }

        if (string.IsNullOrWhiteSpace(scheduledMessage.RenderedBody))
        {
            _logger.LogWarning(
                "ScheduledMessage {MessageId} has empty body, marking as Failed",
                scheduledMessage.Id);

            scheduledMessage.Status = ScheduledMessageStatus.Failed;
            return;
        }

        // Load tenant settings to get sender phone
        TenantSettings? settings = await db.TenantSettings
            .IgnoreQueryFilters() // Cross-tenant access for worker
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == scheduledMessage.TenantId, cancellationToken);

        string senderPhone = settings?.DefaultSenderPhone ?? "+10000000000";

        // Send via provider
        ISmsProvider smsProvider = serviceProvider.GetRequiredService<ISmsProvider>();
        SmsResult result = await smsProvider.SendAsync(
            senderPhone,
            scheduledMessage.Customer.Phone,
            scheduledMessage.RenderedBody,
            cancellationToken);

        if (result.Success)
        {
            scheduledMessage.Status = ScheduledMessageStatus.Sent;
            scheduledMessage.SentAt = DateTime.UtcNow;

            // Log outbound message
            db.Messages.Add(new Message
            {
                TenantId = scheduledMessage.TenantId,
                CustomerId = scheduledMessage.CustomerId,
                Direction = MessageDirection.Outbound,
                Status = MessageStatus.Sent,
                Body = scheduledMessage.RenderedBody,
                FromPhone = senderPhone,
                ToPhone = scheduledMessage.Customer.Phone,
                ProviderMessageId = result.ProviderMessageId,
                SegmentCount = result.SegmentCount
            });

            // Increment usage
            await IncrementUsageAsync(db, scheduledMessage.TenantId, cancellationToken);

            _logger.LogInformation(
                "Dispatched ScheduledMessage {MessageId} to {Phone}",
                scheduledMessage.Id, scheduledMessage.Customer.Phone);
        }
        else
        {
            scheduledMessage.Status = ScheduledMessageStatus.Failed;
            _logger.LogWarning(
                "SMS provider failed for ScheduledMessage {MessageId}: {Error}",
                scheduledMessage.Id, result.ErrorMessage);
        }
    }

    private static async Task IncrementUsageAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;

        Plan? plan = await db.Plans
            .IgnoreQueryFilters() // Cross-tenant access for worker
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && !p.IsDeleted, cancellationToken);

        if (plan is null)
            return;

        UsageRecord? usage = await db.UsageRecords
            .IgnoreQueryFilters() // Cross-tenant access for worker
            .FirstOrDefaultAsync(u => u.PlanId == plan.Id && u.Year == now.Year && u.Month == now.Month && !u.IsDeleted, cancellationToken);

        if (usage is null)
        {
            usage = new UsageRecord
            {
                TenantId = tenantId,
                PlanId = plan.Id,
                Year = now.Year,
                Month = now.Month,
                SmsSent = 1
            };
            db.UsageRecords.Add(usage);
        }
        else
        {
            usage.SmsSent++;
        }
    }
}
