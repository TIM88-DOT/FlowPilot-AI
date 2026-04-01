using FlowPilot.Shared;

namespace FlowPilot.Application.PublicBooking;

/// <summary>
/// Handles public (unauthenticated) booking operations for customer-facing booking pages.
/// Tenant context is resolved from the URL slug by middleware.
/// </summary>
public interface IPublicBookingService
{
    /// <summary>
    /// Returns public business info, services, and business hours for the booking page.
    /// </summary>
    Task<Result<PublicBusinessInfoDto>> GetBusinessInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// Computes available time slots for a given date and service, accounting for
    /// existing appointments, business hours, buffer time, and advance booking rules.
    /// </summary>
    Task<Result<List<TimeSlotDto>>> GetAvailableSlotsAsync(DateTime date, Guid serviceId, CancellationToken ct = default);

    /// <summary>
    /// Creates a booking: finds or creates the customer by phone, then creates
    /// the appointment. Triggers the reminder pipeline via domain events.
    /// </summary>
    Task<Result<PublicBookingConfirmationDto>> BookAsync(PublicBookingRequest request, CancellationToken ct = default);
}
