using FlowPilot.Application.Settings;

namespace FlowPilot.Application.PublicBooking;

/// <summary>
/// Public business info returned for the booking page header and service selection.
/// </summary>
public sealed record PublicBusinessInfoDto(
    string BusinessName,
    string Slug,
    string? BusinessPhone,
    string? BusinessEmail,
    string? Address,
    string? Timezone,
    string Currency,
    BusinessHoursDto? BusinessHours,
    int MinAdvanceHours,
    List<PublicServiceDto> Services);

/// <summary>
/// Minimal service info shown on the public booking page.
/// </summary>
public sealed record PublicServiceDto(
    Guid Id,
    string Name,
    int DurationMinutes,
    decimal? Price,
    string? Currency);

/// <summary>
/// A single available time slot for booking.
/// </summary>
public sealed record TimeSlotDto(
    string StartTime,
    string EndTime);

/// <summary>
/// Request body for creating a public booking.
/// TenantId is resolved from the URL slug by middleware — never from the request body.
/// When <paramref name="RescheduleAppointmentId"/> is set, the booking service reschedules
/// the existing appointment instead of creating a new one. Used by the SMS-driven reschedule
/// flow where the customer clicks a link in an inbound SMS reply.
/// </summary>
public sealed record PublicBookingRequest(
    string FirstName,
    string? LastName,
    string Phone,
    string? Email,
    Guid ServiceId,
    DateTime StartsAt,
    string? Notes,
    string? PreferredLanguage,
    Guid? RescheduleAppointmentId = null);

/// <summary>
/// Confirmation returned after a successful public booking.
/// </summary>
/// <param name="SmsResubscribeRequired">
/// True when the customer previously sent STOP to Twilio. Our DB re-opts them in on booking,
/// but Twilio's carrier-level block remains until the customer texts START to the SMS number.
/// The frontend should display a notice when this is true.
/// </param>
/// <param name="SmsNumber">
/// The Twilio sender number the customer should text START to if SmsResubscribeRequired is true.
/// </param>
public sealed record PublicBookingConfirmationDto(
    Guid AppointmentId,
    string ServiceName,
    DateTime StartsAt,
    DateTime EndsAt,
    string BusinessName,
    string? BusinessPhone,
    bool SmsResubscribeRequired = false,
    string? SmsNumber = null);
