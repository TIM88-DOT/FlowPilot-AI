using FlowPilot.Application.Messaging;
using Microsoft.Extensions.Logging;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Fake SMS provider for local development and testing.
/// Logs messages to the console instead of sending real SMS.
/// Swap to TwilioSmsProvider via DI configuration for production.
/// </summary>
public sealed class FakeSmsProvider : ISmsProvider
{
    private readonly ILogger<FakeSmsProvider> _logger;

    public FakeSmsProvider(ILogger<FakeSmsProvider> logger)
    {
        _logger = logger;
    }

    public Task<SmsResult> SendAsync(string fromPhone, string toPhone, string body, CancellationToken cancellationToken = default)
    {
        string fakeMessageId = $"FAKE_{Guid.NewGuid():N}";

        _logger.LogInformation(
            "[FakeSMS] From: {From} → To: {To} | MessageId: {MessageId} | Body: {Body}",
            fromPhone, toPhone, fakeMessageId, body);

        // Estimate segments: SMS is 160 chars for GSM-7, 70 for UCS-2
        int segmentCount = (int)Math.Ceiling(body.Length / 160.0);

        return Task.FromResult(new SmsResult(
            Success: true,
            ProviderMessageId: fakeMessageId,
            SegmentCount: segmentCount));
    }
}
