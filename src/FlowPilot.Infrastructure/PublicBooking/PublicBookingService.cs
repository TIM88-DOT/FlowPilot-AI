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

    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PublicBookingService(AppDbContext db, ICurrentTenant currentTenant, IAppointmentService appointmentService)
    {
        _db = db;
        _currentTenant = currentTenant;
        _appointmentService = appointmentService;
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

        return Result.Success(new PublicBusinessInfoDto(
            BusinessName: tenant.BusinessName,
            Slug: tenant.Slug,
            BusinessPhone: tenant.BusinessPhone,
            BusinessEmail: tenant.BusinessEmail,
            Address: tenant.Address,
            Timezone: tenant.Timezone,
            Currency: tenant.Currency,
            BusinessHours: businessHours,
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

        BusinessHoursDto? businessHours = Deserialize<BusinessHoursDto>(settings?.BusinessHoursJson);
        BookingSettingsDto? bookingSettings = Deserialize<BookingSettingsDto>(settings?.BookingSettingsJson);

        if (businessHours is null)
            return Result.Success(new List<TimeSlotDto>());

        // Get hours for the requested day
        DayHoursDto? dayHours = GetDayHours(businessHours, date.DayOfWeek);
        if (dayHours is null || !dayHours.Enabled)
            return Result.Success(new List<TimeSlotDto>());

        int bufferMinutes = bookingSettings?.BufferMinutes ?? 0;
        int maxAdvanceDays = bookingSettings?.MaxAdvanceDays ?? 60;
        int minAdvanceHours = bookingSettings?.MinAdvanceHours ?? 2;

        // Validate booking window
        DateTime now = DateTime.UtcNow;
        DateTime dateOnly = date.Date;
        if (dateOnly < now.Date)
            return Result.Failure<List<TimeSlotDto>>(Error.Validation("Booking.PastDate", "Cannot book in the past."));
        if (dateOnly > now.Date.AddDays(maxAdvanceDays))
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
            if (slotEnd > closeTime) break;
            candidates.Add((cursor, slotEnd));
            cursor = cursor.AddMinutes(30);
        }

        if (candidates.Count == 0)
            return Result.Success(new List<TimeSlotDto>());

        // Get existing appointments for the date (exclude cancelled/rescheduled)
        DateTime dayStart = DateTime.SpecifyKind(dateOnly, DateTimeKind.Utc);
        DateTime dayEnd = DateTime.SpecifyKind(dateOnly.AddDays(1), DateTimeKind.Utc);
        List<(DateTime StartsAt, DateTime EndsAt)> existing = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.StartsAt >= dayStart && a.StartsAt < dayEnd
                && a.Status != AppointmentStatus.Cancelled
                && a.Status != AppointmentStatus.Rescheduled)
            .Select(a => new { a.StartsAt, a.EndsAt })
            .ToListAsync(ct)
            .ContinueWith(t => t.Result.Select(a => (a.StartsAt, a.EndsAt)).ToList(), ct);

        // Filter out overlapping slots
        DateTime earliestAllowed = now.AddHours(minAdvanceHours);
        var available = new List<TimeSlotDto>();

        foreach ((TimeOnly slotStart, TimeOnly slotEnd) in candidates)
        {
            DateTime slotStartUtc = DateTime.SpecifyKind(dateOnly.Add(slotStart.ToTimeSpan()), DateTimeKind.Utc);
            DateTime slotEndUtc = DateTime.SpecifyKind(dateOnly.Add(slotEnd.ToTimeSpan()), DateTimeKind.Utc);

            // Skip past slots
            if (slotStartUtc < earliestAllowed) continue;

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

        // Ensure UTC kind for PostgreSQL timestamptz compatibility
        DateTime startsAtUtc = DateTime.SpecifyKind(request.StartsAt, DateTimeKind.Utc);
        DateTime endsAt = DateTime.SpecifyKind(startsAtUtc.AddMinutes(service.DurationMinutes), DateTimeKind.Utc);
        Result<List<TimeSlotDto>> slotsResult = await GetAvailableSlotsAsync(startsAtUtc.Date, request.ServiceId, ct);
        if (slotsResult.IsFailure)
            return Result.Failure<PublicBookingConfirmationDto>(slotsResult.Error);

        string requestedTime = startsAtUtc.ToString("HH:mm");
        bool slotAvailable = slotsResult.Value.Any(s => s.StartTime == requestedTime);
        if (!slotAvailable)
            return Result.Failure<PublicBookingConfirmationDto>(
                Error.Conflict("Booking.SlotUnavailable", "This time slot is no longer available. Please select another time."));

        // Normalize phone
        string? normalizedPhone = NormalizeToE164(request.Phone);
        if (normalizedPhone is null)
            return Result.Failure<PublicBookingConfirmationDto>(
                Error.Validation("Customer.InvalidPhone", "Phone number is not valid. Provide a number with country code (e.g. +213555123456)."));

        // Find or create customer by phone — one phone = one customer identity.
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
            // Returning customer — update name, language, and fill missing fields
            customer.FirstName = request.FirstName.Trim();
            if (!string.IsNullOrWhiteSpace(request.LastName))
                customer.LastName = request.LastName;
            if (!string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(customer.Email))
                customer.Email = request.Email;
            if (!string.IsNullOrWhiteSpace(request.PreferredLanguage))
                customer.PreferredLanguage = request.PreferredLanguage;
            await _db.SaveChangesAsync(ct);
        }

        // Create appointment via existing service — triggers AppointmentCreatedEvent → ReminderOptimizationAgent
        Result<AppointmentDto> appointmentResult = await _appointmentService.CreateAsync(new CreateAppointmentRequest(
            CustomerId: customer.Id,
            StartsAt: startsAtUtc,
            EndsAt: endsAt,
            ServiceName: service.Name,
            Notes: request.Notes), ct);

        if (appointmentResult.IsFailure)
            return Result.Failure<PublicBookingConfirmationDto>(appointmentResult.Error);

        // Load business name for confirmation
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

        return Result.Success(new PublicBookingConfirmationDto(
            AppointmentId: appointmentResult.Value.Id,
            ServiceName: service.Name,
            StartsAt: startsAtUtc,
            EndsAt: endsAt,
            BusinessName: businessName,
            BusinessPhone: businessPhone));
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
            PhoneNumber number = PhoneUtil.Parse(rawPhone, "DZ");
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
