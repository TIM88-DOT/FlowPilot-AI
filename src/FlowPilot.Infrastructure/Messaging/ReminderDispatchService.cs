using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Dispatches due scheduled reminder messages across all tenants. Extracted from the
/// ScheduledMessageDispatcher worker so the dispatch decisions are testable independent of the
/// hosted polling loop. The worker is now a thin scheduler that calls <see cref="DispatchDueAsync"/>.
/// </summary>
public sealed class ReminderDispatchService : IReminderDispatchService
{
    private readonly AppDbContext _db;
    private readonly ISmsProvider _smsProvider;
    private readonly ILogger<ReminderDispatchService> _logger;

    public ReminderDispatchService(
        AppDbContext db,
        ISmsProvider smsProvider,
        ILogger<ReminderDispatchService> logger)
    {
        _db = db;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> DispatchDueAsync(CancellationToken cancellationToken = default)
    {
        // Fetch due messages across all tenants — ignore query filters for cross-tenant dispatch
        List<ScheduledMessage> dueMessages = await _db.ScheduledMessages
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
            return 0;

        _logger.LogInformation("Found {Count} due scheduled messages to dispatch", dueMessages.Count);

        foreach (ScheduledMessage message in dueMessages)
        {
            try
            {
                await DispatchSingleMessageAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to dispatch ScheduledMessage {MessageId} for Tenant {TenantId}",
                    message.Id, message.TenantId);

                message.Status = ScheduledMessageStatus.Failed;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return dueMessages.Count;
    }

    private async Task DispatchSingleMessageAsync(
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
        TenantSettings? settings = await _db.TenantSettings
            .IgnoreQueryFilters() // Cross-tenant access for worker
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == scheduledMessage.TenantId, cancellationToken);

        string senderPhone = settings?.DefaultSenderPhone ?? "+10000000000";

        // Resolve the {time_until} token against the live appointment time at the moment of sending,
        // so the customer always sees the correct remaining time regardless of dispatch delay.
        string body = ReminderTimePhrase.Resolve(
            scheduledMessage.RenderedBody,
            scheduledMessage.Appointment.StartsAt,
            DateTime.UtcNow,
            scheduledMessage.Customer.PreferredLanguage);

        // Send via provider
        SmsResult result = await _smsProvider.SendAsync(
            senderPhone,
            scheduledMessage.Customer.Phone,
            body,
            cancellationToken);

        if (result.Success)
        {
            scheduledMessage.Status = ScheduledMessageStatus.Sent;
            scheduledMessage.SentAt = DateTime.UtcNow;

            // Log outbound message
            _db.Messages.Add(new Message
            {
                TenantId = scheduledMessage.TenantId,
                CustomerId = scheduledMessage.CustomerId,
                Direction = MessageDirection.Outbound,
                Status = MessageStatus.Sent,
                Body = body,
                FromPhone = senderPhone,
                ToPhone = scheduledMessage.Customer.Phone,
                ProviderMessageId = result.ProviderMessageId,
                SegmentCount = result.SegmentCount
            });

            // Increment usage
            await IncrementUsageAsync(scheduledMessage.TenantId, cancellationToken);

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

    private async Task IncrementUsageAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;

        Plan? plan = await _db.Plans
            .IgnoreQueryFilters() // Cross-tenant access for worker
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && !p.IsDeleted, cancellationToken);

        if (plan is null)
            return;

        UsageRecord? usage = await _db.UsageRecords
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
            _db.UsageRecords.Add(usage);
        }
        else
        {
            usage.SmsSent++;
        }
    }
}
