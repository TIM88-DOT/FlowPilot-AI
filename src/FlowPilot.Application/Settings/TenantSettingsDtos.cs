using System.Text.Json.Serialization;

namespace FlowPilot.Application.Settings;

/// <summary>
/// Combined response containing tenant info + all settings sections.
/// </summary>
public sealed record TenantSettingsDto(
    // Business info (from Tenant entity)
    string BusinessName,
    string? BusinessPhone,
    string? BusinessEmail,
    string? Address,
    string? Timezone,
    string DefaultLanguage,
    string Currency,
    // Sender phone (from TenantSettings)
    string? DefaultSenderPhone,
    int ReminderLeadTimeMinutes,
    // Review platforms (from TenantSettings)
    string? GooglePlaceId,
    string? FacebookPageUrl,
    string? TrustpilotUrl,
    // JSON sub-sections
    BusinessHoursDto? BusinessHours,
    NotificationSettingsDto? Notifications,
    ReviewSettingsDto? ReviewSettings,
    BookingSettingsDto? BookingSettings);

/// <summary>
/// Request to update tenant + settings in one call.
/// All fields are optional — only provided fields are updated.
/// </summary>
public sealed record UpdateTenantSettingsRequest(
    // Business info
    string? BusinessName = null,
    string? BusinessPhone = null,
    string? BusinessEmail = null,
    string? Address = null,
    string? Timezone = null,
    string? DefaultLanguage = null,
    string? Currency = null,
    // Sender phone
    string? DefaultSenderPhone = null,
    int? ReminderLeadTimeMinutes = null,
    // Review platforms
    string? GooglePlaceId = null,
    string? FacebookPageUrl = null,
    string? TrustpilotUrl = null,
    // JSON sub-sections
    BusinessHoursDto? BusinessHours = null,
    NotificationSettingsDto? Notifications = null,
    ReviewSettingsDto? ReviewSettings = null,
    BookingSettingsDto? BookingSettings = null);

/// <summary>
/// Business hours per day of week.
/// </summary>
public sealed record BusinessHoursDto(
    DayHoursDto Monday,
    DayHoursDto Tuesday,
    DayHoursDto Wednesday,
    DayHoursDto Thursday,
    DayHoursDto Friday,
    DayHoursDto Saturday,
    DayHoursDto Sunday);

public sealed record DayHoursDto(
    bool Enabled,
    string Open,
    string Close);

/// <summary>
/// Notification preferences for the tenant.
/// </summary>
public sealed record NotificationSettingsDto(
    [property: JsonPropertyName("reminderTiming")] int ReminderTimingHours,
    [property: JsonPropertyName("secondReminder")] bool SecondReminder,
    [property: JsonPropertyName("secondReminderTiming")] double SecondReminderTimingHours,
    [property: JsonPropertyName("confirmationEnabled")] bool ConfirmationEnabled,
    [property: JsonPropertyName("noShowFollowUp")] bool NoShowFollowUp,
    [property: JsonPropertyName("smsSignature")] string? SmsSignature);

/// <summary>
/// Review request preferences.
/// </summary>
public sealed record ReviewSettingsDto(
    [property: JsonPropertyName("reviewDelayHours")] int ReviewDelayHours,
    [property: JsonPropertyName("reviewCooldownDays")] int ReviewCooldownDays,
    [property: JsonPropertyName("autoSend")] bool AutoSend);

/// <summary>
/// Booking rules.
/// </summary>
public sealed record BookingSettingsDto(
    [property: JsonPropertyName("bufferMinutes")] int BufferMinutes,
    [property: JsonPropertyName("maxAdvanceDays")] int MaxAdvanceDays,
    [property: JsonPropertyName("minAdvanceHours")] int MinAdvanceHours,
    [property: JsonPropertyName("allowCancel")] bool AllowCancel,
    [property: JsonPropertyName("cancelBeforeHours")] int CancelBeforeHours,
    [property: JsonPropertyName("allowReschedule")] bool AllowReschedule,
    [property: JsonPropertyName("rescheduleBeforeHours")] int RescheduleBeforeHours);
