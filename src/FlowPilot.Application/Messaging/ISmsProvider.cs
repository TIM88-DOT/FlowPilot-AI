namespace FlowPilot.Application.Messaging;

/// <summary>
/// Result returned by the SMS provider after attempting to send a message.
/// </summary>
public sealed record SmsResult(
    bool Success,
    string? ProviderMessageId,
    int? SegmentCount,
    string? ErrorMessage = null);

/// <summary>
/// Pluggable SMS provider interface. Swap implementations via DI configuration.
/// Production: TwilioSmsProvider. Dev/Test: FakeSmsProvider.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Sends an SMS message and returns provider-specific identifiers.
    /// </summary>
    Task<SmsResult> SendAsync(string fromPhone, string toPhone, string body, CancellationToken cancellationToken = default);
}
