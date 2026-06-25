using FlowPilot.Application.Appointments;
using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Sends a "we missed you" follow-up SMS when an appointment is marked Missed (no-show),
/// inviting the customer to rebook.
/// <para>
/// Published by the lifecycle worker's overdue scan, this runs in a cross-tenant worker context
/// with no ambient tenant — so it talks to <see cref="ISmsProvider"/> directly and scopes every
/// query by the event's TenantId via <c>IgnoreQueryFilters</c>, exactly like
/// <see cref="ReminderDispatchService"/>. (It must NOT route through the tenant-scoped
/// <c>IMessagingService</c>, whose global query filter would hide the customer.)
/// </para>
/// Skips silently if the customer is not opted in (consent gate stays deterministic in C#).
/// </summary>
public sealed class NoShowFollowUpSmsHandler : INotificationHandler<AppointmentMissedEvent>
{
    private readonly AppDbContext _db;
    private readonly ISmsProvider _smsProvider;
    private readonly ILogger<NoShowFollowUpSmsHandler> _logger;

    public NoShowFollowUpSmsHandler(
        AppDbContext db,
        ISmsProvider smsProvider,
        ILogger<NoShowFollowUpSmsHandler> logger)
    {
        _db = db;
        _smsProvider = smsProvider;
        _logger = logger;
    }

    public async Task Handle(AppointmentMissedEvent notification, CancellationToken cancellationToken)
    {
        Customer? customer = await _db.Customers
            .AsNoTracking()
            .IgnoreQueryFilters() // Worker context has no ambient tenant — scope by the event's IDs
            .FirstOrDefaultAsync(
                c => c.Id == notification.CustomerId && c.TenantId == notification.TenantId && !c.IsDeleted,
                cancellationToken);

        if (customer is null)
        {
            _logger.LogWarning("NoShowFollowUpSmsHandler: Customer {Id} not found", notification.CustomerId);
            return;
        }

        // Consent gate — deterministic C#, never delegated. Skip silently if not opted in.
        if (customer.ConsentStatus != ConsentStatus.OptedIn)
        {
            _logger.LogDebug(
                "Skipping no-show follow-up SMS for customer {CustomerId} — consent is {Status}",
                customer.Id, customer.ConsentStatus);
            return;
        }

        Tenant? tenant = await _db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters() // Tenant table has no TenantId column — self-referencing
            .FirstOrDefaultAsync(t => t.Id == notification.TenantId, cancellationToken);

        TenantSettings? settings = await _db.TenantSettings
            .AsNoTracking()
            .IgnoreQueryFilters() // Cross-tenant access for worker
            .FirstOrDefaultAsync(s => s.TenantId == notification.TenantId, cancellationToken);

        string businessName = tenant?.BusinessName ?? "our office";
        string senderPhone = settings?.DefaultSenderPhone ?? "+10000000000";
        string body = BuildNoShowBody(customer.PreferredLanguage ?? "fr", customer.FirstName, businessName);

        SmsResult result = await _smsProvider.SendAsync(senderPhone, customer.Phone, body, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning(
                "SMS provider failed for no-show follow-up to customer {CustomerId}: {Error}",
                customer.Id, result.ErrorMessage);
            return;
        }

        _db.Messages.Add(new Message
        {
            TenantId = notification.TenantId,
            CustomerId = customer.Id,
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent,
            Body = body,
            FromPhone = senderPhone,
            ToPhone = customer.Phone,
            ProviderMessageId = result.ProviderMessageId,
            SegmentCount = result.SegmentCount
        });

        await _db.SaveChangesAsync(cancellationToken);
        await UsageTracker.IncrementSmsSentAsync(_db, notification.TenantId, cancellationToken);

        _logger.LogInformation(
            "Sent no-show follow-up SMS to customer {CustomerId} (tenant {TenantId})",
            customer.Id, notification.TenantId);
    }

    private static string BuildNoShowBody(string lang, string firstName, string businessName) =>
        lang switch
        {
            "en" => $"Hi {firstName}, we missed you at your appointment with {businessName}. " +
                    "Would you like to rebook? Reply to this message and we'll help you find a new time.",

            // Default: French
            _ => $"Bonjour {firstName}, nous avons remarqué votre absence à votre RDV chez {businessName}. " +
                 "Souhaitez-vous reprendre rendez-vous ? Répondez à ce message et nous trouverons un nouveau créneau."
        };
}
