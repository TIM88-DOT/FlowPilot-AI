using System.Text.Json;
using FlowPilot.Application.Appointments;
using FlowPilot.Application.PublicBooking;
using FlowPilot.Application.Settings;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhoneNumbers;

namespace FlowPilot.Infrastructure.PublicBooking;

/// <summary>
/// Implements public (unauthenticated) booking operations.
/// Relies on PublicTenantMiddleware having set the TenantId via HttpContext.Items
/// so that EF global filters scope all queries to the correct tenant.
/// </summary>
public sealed class PublicBookingService : IPublicBookingService
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAppointmentService _appointmentService;
    private readonly ILogger<PublicBookingService> _logger;

    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PublicBookingService(AppDbContext db, ICurrentTenant currentTenant, IAppointmentService appointmentService, ILogger<PublicBookingService> logger)
    {
        _db = db;
        _currentTenant = currentTenant;
        _appointmentService = appointmentService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<PublicBusinessInfoDto>> GetBusinessInfoAsync(CancellationToken ct = default)
    {
        Tenant? tenant = await _db.Tenants
            .AsNoTracking()
            .Include(t => t.Settings)
            .FirstOrDefaultAsync(t => t.Id == _currentTenant.TenantId, ct);

        if (tenant is null)
            return Result.Failure<PublicBusinessInfoDto>(Error.NotFound("Tenant", _currentTenant.TenantId));

        List<PublicServiceDto> services = await _db.Services
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new PublicServiceDto(s.Id, s.Name, s.DurationMinutes, s.Price, s.Currency))
            .ToListAsync(ct);

        BusinessHoursDto? businessHours = Deserialize<BusinessHoursDto>(tenant.Settings?.BusinessHoursJson);
        BookingSettingsDto? bookingSettings = Deserialize<BookingSettingsDto>(tenant.Settings?.BookingSettingsJson);

        return Result.Success(new PublicBusinessInfoDto(
            BusinessName: tenant.BusinessName,
            Slug: tenant.Slug,
            BusinessPhone: tenant.BusinessPhone,
            BusinessEmail: tenant.BusinessEmail,
            Address: tenant.Address,
            Timezone: tenant.Timezone,
            Currency: tenant.Currency,
            BusinessHours: businessHours,
            MinAdvanceHours: bookingSettings?.MinAdvanceHours ?? 2,
            Services: services));
    }

    /// <inheritdoc />
    public async Task<Result<List<TimeSlotDto>>> GetAvailableSlotsAsync(DateTime date, Guid serviceId, CancellationToken ct = default)
    {
        // Load service
        Service? service = await _db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.IsActive, ct);

        if (service is null)
            return Result.Failure<List<TimeSlotDto>>(Error.NotFound("Service", serviceId));

        // Load tenant settings
        TenantSettings? settings = await _db.TenantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerTenantId == _currentTenant.TenantId, ct);

        // Load tenant timezone — business hours are in the tenant's local time
        Tenant? tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _currentTenant.TenantId, ct);
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tenant?.Timezone ?? "UTC");
        DateTime nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

        BusinessHoursDto? businessHours = Deserialize<BusinessHoursDto>(settings?.BusinessHoursJson);
        BookingSettingsDto? bookingSettings = Deserialize<BookingSettingsDto>(settings?.BookingSettingsJson);

        if (businessHours is null)
            return Result.Success(new List<TimeSlotDto>());

        // Interpret the requested date in the tenant's local timezone
        DateTime dateOnly = date.Date;

        // Get hours for the requested day
        DayHoursDto? dayHours = GetDayHours(businessHours, dateOnly.DayOfWeek);
        if (dayHours is null || !dayHours.Enabled)
            return Result.Success(new List<TimeSlotDto>());

        int bufferMinutes = bookingSettings?.BufferMinutes ?? 0;
        int maxAdvanceDays = bookingSettings?.MaxAdvanceDays ?? 60;
        int minAdvanceHours = bookingSettings?.MinAdvanceHours ?? 2;

        // Validate booking window using tenant-local "today"
        if (dateOnly < nowLocal.Date)
            return Result.Failure<List<TimeSlotDto>>(Error.Validation("Booking.PastDate", "Cannot book in the past."));
        if (dateOnly > nowLocal.Date.AddDays(maxAdvanceDays))
            return Result.Failure<List<TimeSlotDto>>(Error.Validation("Booking.TooFarAhead", $"Cannot book more than {maxAdvanceDays} days in advance."));

        // Parse business hours
        if (!TimeOnly.TryParse(dayHours.Open, out TimeOnly openTime) ||
            !TimeOnly.TryParse(dayHours.Close, out TimeOnly closeTime))
            return Result.Success(new List<TimeSlotDto>());

        int durationMinutes = service.DurationMinutes;

        // Generate candidate slots (every 30 min)
        var candidates = new List<(TimeOnly Start, TimeOnly End)>();
        TimeOnly cursor = openTime;
        while (true)
        {
            TimeOnly slotEnd = cursor.AddMinutes(durationMinutes);
            if (slotEnd > closeTime || slotEnd < cursor) break; // past close or wrapped midnight
            candidates.Add((cursor, slotEnd));
            TimeOnly next = cursor.AddMinutes(30);
            if (next <= cursor) break; // wrapped past midnight
            cursor = next;
        }

        if (candidates.Count == 0)
            return Result.Success(new List<TimeSlotDto>());

        // Convert day boundaries to UTC for querying appointments
        DateTime dayStartLocal = dateOnly;
        DateTime dayEndLocal = dateOnly.AddDays(1);
        DateTime dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dayStartLocal, DateTimeKind.Unspecified), tz);
        DateTime dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(dayEndLocal, DateTimeKind.Unspecified), tz);

        List<(DateTime StartsAt, DateTime EndsAt)> existing = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.StartsAt >= dayStartUtc && a.StartsAt < dayEndUtc
                && a.Status != AppointmentStatus.Cancelled
                && a.Status != AppointmentStatus.Rescheduled)
            .Select(a => new { a.StartsAt, a.EndsAt })
            .ToListAsync(ct)
            .ContinueWith(t => t.Result.Select(a => (a.StartsAt, a.EndsAt)).ToList(), ct);

        // Filter slots: convert each local slot to UTC, skip past ones, check overlaps
        DateTime earliestAllowedUtc = DateTime.UtcNow.AddHours(minAdvanceHours);
        var available = new List<TimeSlotDto>();

        foreach ((TimeOnly slotStart, TimeOnly slotEnd) in candidates)
        {
            // Convert local slot times to UTC for comparison
            DateTime slotStartLocal = DateTime.SpecifyKind(dateOnly.Add(slotStart.ToTimeSpan()), DateTimeKind.Unspecified);
            DateTime slotEndLocal = DateTime.SpecifyKind(dateOnly.Add(slotEnd.ToTimeSpan()), DateTimeKind.Unspecified);
            DateTime slotStartUtc = TimeZoneInfo.ConvertTimeToUtc(slotStartLocal, tz);
            DateTime slotEndUtc = TimeZoneInfo.ConvertTimeToUtc(slotEndLocal, tz);

            // Skip past slots
            if (slotStartUtc < earliestAllowedUtc) continue;

            // Check overlap with existing appointments (including buffer)
            bool overlaps = existing.Any(e =>
            {
                DateTime bufferedStart = e.StartsAt.AddMinutes(-bufferMinutes);
                DateTime bufferedEnd = e.EndsAt.AddMinutes(bufferMinutes);
                return slotStartUtc < bufferedEnd && slotEndUtc > bufferedStart;
            });

            if (!overlaps)
                available.Add(new TimeSlotDto(slotStart.ToString("HH:mm"), slotEnd.ToString("HH:mm")));
        }

        return Result.Success(available);
    }

    /// <inheritdoc />
    public async Task<Result<PublicBookingConfirmationDto>> BookAsync(PublicBookingRequest request, CancellationToken ct = default)
    {
        // Validate service
        Service? service = await _db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.IsActive, ct);

        if (service is null)
            return Result.Failure<PublicBookingConfirmationDto>(Error.NotFound("Service", request.ServiceId));

        // Convert local tenant time to UTC — business hours and slot times are in the tenant's timezone
        Tenant? tenant = await _db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _currentTenant.TenantId, ct);
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(tenant?.Timezone ?? "UTC");
        _logger.LogInformation(
            "BookAsync: request.StartsAt={StartsAt} Kind={Kind}, TenantTz={Tz}",
            request.StartsAt, request.StartsAt.Kind, tz.Id);
        DateTime startsAtLocal = DateTime.SpecifyKind(request.StartsAt, DateTimeKind.Unspecified);
        DateTime startsAtUtc = TimeZoneInfo.ConvertTimeToUtc(startsAtLocal, tz);
        DateTime endsAt = startsAtUtc.AddMinutes(service.DurationMinutes);
        _logger.LogInformation(
            "BookAsync: startsAtLocal={Local}, startsAtUtc={Utc}, endsAt={End}",
            startsAtLocal, startsAtUtc, endsAt);
        Result<List<TimeSlotDto>> slotsResult = await GetAvailableSlotsAsync(startsAtLocal.Date, request.ServiceId, ct);
        if (slotsResult.IsFailure)
            return Result.Failure<PublicBookingConfirmationDto>(slotsResult.Error);

        string requestedTime = startsAtLocal.ToString("HH:mm");
        bool slotAvailable = slotsResult.Value.Any(s => s.StartTime == requestedTime);
        if (!slotAvailable)
            return Result.Failure<PublicBookingConfirmationDto>(
                Error.Conflict("Booking.SlotUnavailable", "This time slot is no longer available. Please select another time."));

        // Normalize phone
        string? normalizedPhone = NormalizeToE164(request.Phone);
        if (normalizedPhone is null)
            return Result.Failure<PublicBookingConfirmationDto>(
                Error.Validation("Customer.InvalidPhone", "Phone number is not valid. Provide a number with country code (e.g. +14165551234)."));

        // Find or create customer by phone — one phone = one customer identity.
        // Track whether the customer was previously opted out on Twilio's side.
        // Our DB re-opts them in on booking, but Twilio's carrier-level block remains
        // until the customer texts START to the business number.
        bool smsResubscribeRequired = false;

        Customer? customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Phone == normalizedPhone, ct);

        if (customer is null)
        {
            customer = new Customer
            {
                Phone = normalizedPhone,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName,
                Email = request.Email,
                PreferredLanguage = request.PreferredLanguage ?? "fr",
                ConsentStatus = ConsentStatus.OptedIn
            };

            _db.Customers.Add(customer);

            _db.ConsentRecords.Add(new ConsentRecord
            {
                CustomerId = customer.Id,
                Status = ConsentStatus.OptedIn,
                Source = ConsentSource.Booking,
                Notes = "Customer opted in by self-booking via public booking page"
            });

            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // Returning customer — only fill empty fields, never overwrite existing data.
            // Staff-managed fields (name, email) take precedence over self-service input.
            bool changed = false;
            smsResubscribeRequired = customer.ConsentStatus == ConsentStatus.OptedOut;

            if (string.IsNullOrWhiteSpace(customer.FirstName))
            {
                customer.FirstName = request.FirstName.Trim();
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(customer.LastName) && !string.IsNullOrWhiteSpace(request.LastName))
            {
                customer.LastName = request.LastName;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(customer.Email) && !string.IsNullOrWhiteSpace(request.Email))
            {
                customer.Email = request.Email;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(customer.PreferredLanguage) && !string.IsNullOrWhiteSpace(request.PreferredLanguage))
            {
                customer.PreferredLanguage = request.PreferredLanguage;
                changed = true;
            }

            // Re-opt in if customer had previously opted out and is now booking again
            if (customer.ConsentStatus != ConsentStatus.OptedIn)
            {
                customer.ConsentStatus = ConsentStatus.OptedIn;
                _db.ConsentRecords.Add(new ConsentRecord
                {
                    CustomerId = customer.Id,
                    Status = ConsentStatus.OptedIn,
                    Source = ConsentSource.Booking,
                    Notes = "Customer re-opted in by self-booking via public booking page"
                });
                changed = true;
            }

            if (changed)
                await _db.SaveChangesAsync(ct);
        }

        // Create or reschedule via the existing appointment service.
        // Reschedule path is used by the SMS-driven reschedule flow: the customer receives an
        // inbound SMS link carrying the appointment id, opens the booking page with
        // ?reschedule={id}, and picks a new slot. We verify the target appointment belongs
        // to the customer resolved by phone — otherwise it would be a cross-customer rewrite.
        Result<AppointmentDto> appointmentResult;

        if (request.RescheduleAppointmentId is Guid rescheduleId)
        {
            Appointment? existing = await _db.Appointments
                .FirstOrDefaultAsync(a => a.Id == rescheduleId, ct);

            if (existing is null)
                return Result.Failure<PublicBookingConfirmationDto>(Error.NotFound("Appointment", rescheduleId));

            if (existing.CustomerId != customer.Id)
                return Result.Failure<PublicBookingConfirmationDto>(
                    Error.Forbidden("This appointment does not belong to the provided phone number."));

            appointmentResult = await _appointmentService.RescheduleAsync(
                rescheduleId,
                new RescheduleAppointmentRequest(startsAtUtc, endsAt),
                ct);
        }
        else
        {
            // Create appointment via existing service — triggers AppointmentCreatedEvent → ReminderOptimizationAgent
            appointmentResult = await _appointmentService.CreateAsync(new CreateAppointmentRequest(
                CustomerId: customer.Id,
                StartsAt: startsAtUtc,
                EndsAt: endsAt,
                ServiceName: service.Name,
                Notes: request.Notes), ct);
        }

        if (appointmentResult.IsFailure)
            return Result.Failure<PublicBookingConfirmationDto>(appointmentResult.Error);

        // Load business name and SMS sender phone for confirmation
        string businessName = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == _currentTenant.TenantId)
            .Select(t => t.BusinessName)
            .FirstOrDefaultAsync(ct) ?? "";

        string? businessPhone = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == _currentTenant.TenantId)
            .Select(t => t.BusinessPhone)
            .FirstOrDefaultAsync(ct);

        string? smsNumber = smsResubscribeRequired
            ? await _db.TenantSettings.AsNoTracking()
                .Where(s => s.OwnerTenantId == _currentTenant.TenantId)
                .Select(s => s.DefaultSenderPhone)
                .FirstOrDefaultAsync(ct)
            : null;

        return Result.Success(new PublicBookingConfirmationDto(
            AppointmentId: appointmentResult.Value.Id,
            ServiceName: service.Name,
            StartsAt: startsAtUtc,
            EndsAt: endsAt,
            BusinessName: businessName,
            BusinessPhone: businessPhone,
            SmsResubscribeRequired: smsResubscribeRequired,
            SmsNumber: smsNumber));
    }

    private static DayHoursDto? GetDayHours(BusinessHoursDto hours, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => hours.Monday,
        DayOfWeek.Tuesday => hours.Tuesday,
        DayOfWeek.Wednesday => hours.Wednesday,
        DayOfWeek.Thursday => hours.Thursday,
        DayOfWeek.Friday => hours.Friday,
        DayOfWeek.Saturday => hours.Saturday,
        DayOfWeek.Sunday => hours.Sunday,
        _ => null
    };

    private static string? NormalizeToE164(string rawPhone)
    {
        try
        {
            PhoneNumber number = PhoneUtil.Parse(rawPhone, "CA");
            if (!PhoneUtil.IsValidNumber(number))
                return null;
            return PhoneUtil.Format(number, PhoneNumberFormat.E164);
        }
        catch (NumberParseException)
        {
            return null;
        }
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
