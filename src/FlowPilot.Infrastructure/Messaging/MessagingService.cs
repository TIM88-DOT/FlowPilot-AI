using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Orchestrates SMS sending: consent gate → render template → send via ISmsProvider → log Message → update UsageRecord.
/// Also handles inbound SMS processing with STOP keyword opt-out.
/// </summary>
public sealed class MessagingService : IMessagingService
{
    private readonly AppDbContext _db;
    private readonly ISmsProvider _smsProvider;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly ICurrentTenant _currentTenant;
    private readonly IMediator _mediator;
    private readonly ILogger<MessagingService> _logger;

    /// <summary>
    /// Keywords that trigger automatic opt-out (case-insensitive).
    /// </summary>
    private static readonly HashSet<string> StopKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "STOP", "UNSUBSCRIBE", "CANCEL", "END", "QUIT", "ARRET", "ARRETER"
    };

    public MessagingService(
        AppDbContext db,
        ISmsProvider smsProvider,
        ITemplateRenderer templateRenderer,
        ICurrentTenant currentTenant,
        IMediator mediator,
        ILogger<MessagingService> logger)
    {
        _db = db;
        _smsProvider = smsProvider;
        _templateRenderer = templateRenderer;
        _currentTenant = currentTenant;
        _mediator = mediator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<SendSmsResponse>> SendTemplatedAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        // Load customer and check consent
        Customer? customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);

        if (customer is null)
            return Result.Failure<SendSmsResponse>(Error.NotFound("Customer", request.CustomerId));

        Result consentCheck = CheckConsent(customer);
        if (consentCheck.IsFailure)
            return Result.Failure<SendSmsResponse>(consentCheck.Error);

        // Render template with customer's preferred language
        string? renderedBody = await _templateRenderer.RenderAsync(
            request.TemplateId, customer.PreferredLanguage, request.Variables, cancellationToken);

        if (renderedBody is null)
            return Result.Failure<SendSmsResponse>(Error.Validation("Messaging.TemplateNotFound", "Template or locale variant not found."));

        return await SendCoreAsync(customer, renderedBody, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<SendSmsResponse>> SendRawAsync(SendRawSmsRequest request, CancellationToken cancellationToken = default)
    {
        Customer? customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);

        if (customer is null)
            return Result.Failure<SendSmsResponse>(Error.NotFound("Customer", request.CustomerId));

        Result consentCheck = CheckConsent(customer);
        if (consentCheck.IsFailure)
            return Result.Failure<SendSmsResponse>(consentCheck.Error);

        return await SendCoreAsync(customer, request.Body, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> ProcessInboundAsync(InboundSmsWebhook webhook, CancellationToken cancellationToken = default)
    {
        // Idempotency: check if we already processed this SmsSid
        bool alreadyProcessed = await _db.Messages
            .IgnoreQueryFilters() // SmsSid is globally unique across tenants
            .AnyAsync(m => m.ProviderSmsSid == webhook.ProviderSmsSid, cancellationToken);

        if (alreadyProcessed)
            return Result.Success();

        // Find customer by phone number (within tenant)
        Customer? customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Phone == webhook.FromPhone, cancellationToken);

        if (customer is null)
        {
            _logger.LogWarning("Inbound SMS from unknown phone {Phone}, SmsSid {SmsSid}",
                webhook.FromPhone, webhook.ProviderSmsSid);
            return Result.Success(); // Don't fail — just log and move on
        }

        // STOP keyword handling — synchronous opt-out BEFORE any other processing
        string normalizedBody = webhook.Body.Trim();
        if (StopKeywords.Contains(normalizedBody))
        {
            customer = await _db.Customers
                .FirstAsync(c => c.Id == customer.Id, cancellationToken);

            customer.ConsentStatus = ConsentStatus.OptedOut;

            _db.ConsentRecords.Add(new ConsentRecord
            {
                CustomerId = customer.Id,
                Status = ConsentStatus.OptedOut,
                Source = ConsentSource.SmsOptOut,
                Notes = $"Customer sent: {normalizedBody}"
            });

            _logger.LogInformation("Customer {CustomerId} opted out via SMS keyword: {Keyword}",
                customer.Id, normalizedBody);

            await _mediator.Publish(new CustomerOptedOutEvent(customer.Id, _currentTenant.TenantId), cancellationToken);
        }

        // Log the inbound message
        var message = new Message
        {
            CustomerId = customer.Id,
            Direction = MessageDirection.Inbound,
            Status = MessageStatus.Received,
            Body = webhook.Body,
            FromPhone = webhook.FromPhone,
            ToPhone = webhook.ToPhone,
            ProviderSmsSid = webhook.ProviderSmsSid
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ProcessDeliveryStatusAsync(DeliveryStatusWebhook webhook, CancellationToken cancellationToken = default)
    {
        // Find the outbound message by ProviderMessageId
        Message? message = await _db.Messages
            .FirstOrDefaultAsync(m => m.ProviderMessageId == webhook.ProviderMessageId, cancellationToken);

        if (message is null)
        {
            _logger.LogWarning("Delivery status for unknown ProviderMessageId {Id}", webhook.ProviderMessageId);
            return Result.Success();
        }

        // Map provider status to our enum
        MessageStatus newStatus = webhook.Status.ToLower() switch
        {
            "delivered" => MessageStatus.Delivered,
            "sent" => MessageStatus.Sent,
            "failed" or "undelivered" => MessageStatus.Failed,
            _ => message.Status // Unknown status — keep current
        };

        // Only update if the new status is "more final" than the current one
        if (newStatus > message.Status)
        {
            message.Status = newStatus;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Hard consent gate. Blocks SMS to any customer who is not explicitly OptedIn.
    /// </summary>
    private static Result CheckConsent(Customer customer)
    {
        if (customer.ConsentStatus != ConsentStatus.OptedIn)
        {
            return Result.Failure(Error.Validation(
                "Messaging.ConsentRequired",
                $"Cannot send SMS to customer '{customer.Id}'. Consent status is '{customer.ConsentStatus}'. Customer must be OptedIn."));
        }

        return Result.Success();
    }

    /// <summary>
    /// Core send logic shared between templated and raw sends.
    /// Resolves sender phone → sends via provider → logs message → increments usage.
    /// </summary>
    private async Task<Result<SendSmsResponse>> SendCoreAsync(Customer customer, string body, CancellationToken cancellationToken)
    {
        // Get sender phone from tenant settings
        TenantSettings? settings = await _db.TenantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        string senderPhone = settings?.DefaultSenderPhone ?? "+10000000000"; // Fake default for dev

        // Send via provider
        SmsResult smsResult = await _smsProvider.SendAsync(senderPhone, customer.Phone, body, cancellationToken);

        // Log the outbound message regardless of success/failure
        var message = new Message
        {
            CustomerId = customer.Id,
            Direction = MessageDirection.Outbound,
            Status = smsResult.Success ? MessageStatus.Sent : MessageStatus.Failed,
            Body = body,
            FromPhone = senderPhone,
            ToPhone = customer.Phone,
            ProviderMessageId = smsResult.ProviderMessageId,
            SegmentCount = smsResult.SegmentCount
        };

        _db.Messages.Add(message);

        // Increment usage record
        if (smsResult.Success)
        {
            await IncrementUsageAsync(cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (!smsResult.Success)
        {
            return Result.Failure<SendSmsResponse>(Error.Validation(
                "Messaging.SendFailed",
                smsResult.ErrorMessage ?? "SMS provider failed to send the message."));
        }

        return Result.Success(new SendSmsResponse(
            message.Id, smsResult.ProviderMessageId, body, smsResult.SegmentCount));
    }

    /// <summary>
    /// Increments the SmsSent counter on the current month's UsageRecord.
    /// Creates the record if it doesn't exist yet.
    /// </summary>
    private async Task IncrementUsageAsync(CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        Plan? plan = await _db.Plans
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
            return;

        UsageRecord? usage = await _db.UsageRecords
            .FirstOrDefaultAsync(u => u.PlanId == plan.Id && u.Year == now.Year && u.Month == now.Month, cancellationToken);

        if (usage is null)
        {
            usage = new UsageRecord
            {
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
