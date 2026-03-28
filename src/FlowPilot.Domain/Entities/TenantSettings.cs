using FlowPilot.Domain.Common;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Tenant-specific settings including review platform configuration.
/// </summary>
public class TenantSettings : BaseEntity
{
    public Guid OwnerTenantId { get; set; }

    // Review platform fields
    public string? GooglePlaceId { get; set; }
    public string? FacebookPageUrl { get; set; }
    public string? TrustpilotUrl { get; set; }

    // Business hours (stored as JSON or simple fields)
    public string? BusinessHoursJson { get; set; }

    // Messaging defaults
    public string? DefaultSenderPhone { get; set; }
    public int ReminderLeadTimeMinutes { get; set; } = 120;

    // JSON settings for configurable sub-sections
    public string? NotificationSettingsJson { get; set; }
    public string? ReviewSettingsJson { get; set; }
    public string? BookingSettingsJson { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
