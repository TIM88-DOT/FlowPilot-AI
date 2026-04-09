using FlowPilot.Application.Appointments;
using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Sends an instant confirmation SMS when an appointment is booked.
/// Skips silently if customer has not opted in (e.g. consent still Pending).
/// </summary>
public sealed class AppointmentBookedSmsHandler : INotificationHandler<AppointmentCreatedEvent>
{
    private readonly AppDbContext _db;
    private readonly IMessagingService _messagingService;
    private readonly ILogger<AppointmentBookedSmsHandler> _logger;

    public AppointmentBookedSmsHandler(
        AppDbContext db,
        IMessagingService messagingService,
        ILogger<AppointmentBookedSmsHandler> logger)
    {
        _db = db;
        _messagingService = messagingService;
        _logger = logger;
    }

    public async Task Handle(AppointmentCreatedEvent notification, CancellationToken cancellationToken)
    {
        Appointment? appointment = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == notification.AppointmentId, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning("AppointmentBookedSmsHandler: Appointment {Id} not found", notification.AppointmentId);
            return;
        }

        Customer customer = appointment.Customer;

        // Don't attempt if customer hasn't opted in — consent gate in SendRawAsync
        // would reject it anyway, but we skip to avoid noisy error logs.
        if (customer.ConsentStatus != ConsentStatus.OptedIn)
        {
            _logger.LogDebug(
                "Skipping booking confirmation SMS for customer {CustomerId} — consent is {Status}",
                customer.Id, customer.ConsentStatus);
            return;
        }

        // Load business name for the SMS body
        Tenant? tenant = await _db.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters() // Tenant table has no TenantId column — self-referencing
            .FirstOrDefaultAsync(t => t.Id == notification.TenantId, cancellationToken);

        string businessName = tenant?.BusinessName ?? "our office";
        string lang = customer.PreferredLanguage ?? "fr";

        // Convert UTC appointment time to tenant's local timezone for the SMS body
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tenant?.Timezone ?? "UTC");
        DateTime startsAtLocal = TimeZoneInfo.ConvertTimeFromUtc(notification.StartsAt, tz);

        string body = BuildConfirmationBody(lang, customer.FirstName, appointment.ServiceName, businessName, startsAtLocal);

        var request = new SendRawSmsRequest(customer.Id, body);
        Result<SendSmsResponse> result = await _messagingService.SendRawAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to send booking confirmation SMS to customer {CustomerId}: {Error}",
                customer.Id, result.Error.Description);
            return;
        }

        _logger.LogInformation(
            "Sent booking confirmation SMS to customer {CustomerId}, MessageId={MessageId}",
            customer.Id, result.Value.MessageId);
    }

    private static string BuildConfirmationBody(
        string lang, string firstName, string? serviceName, string businessName, DateTime startsAt)
    {
        string service = string.IsNullOrWhiteSpace(serviceName) ? "" : serviceName;

        return lang switch
        {
            "en" => string.IsNullOrEmpty(service)
                ? $"Hi {firstName}, your appointment at {businessName} on {startsAt:dddd, MMM d 'at' h:mm tt} is scheduled. Reply YES or CONFIRM to confirm!"
                : $"Hi {firstName}, your appointment for {service} at {businessName} on {startsAt:dddd, MMM d 'at' h:mm tt} is scheduled. Reply YES or CONFIRM to confirm!",

            // Default: French
            _ => string.IsNullOrEmpty(service)
                ? $"Bonjour {firstName}, votre RDV chez {businessName} le {startsAt:dddd d MMM à HH:mm} est planifié. Répondez OUI pour confirmer !"
                : $"Bonjour {firstName}, votre RDV ({service}) chez {businessName} le {startsAt:dddd d MMM à HH:mm} est planifié. Répondez OUI pour confirmer !"
        };
    }
}
