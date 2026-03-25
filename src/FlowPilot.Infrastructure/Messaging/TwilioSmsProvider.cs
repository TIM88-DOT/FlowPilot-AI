using FlowPilot.Application.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FlowPilot.Infrastructure.Messaging;

/// <summary>
/// Twilio SMS provider. Uses the Twilio REST API directly (no SDK dependency).
/// Configure via appsettings: Twilio:AccountSid, Twilio:AuthToken.
/// Swap in via DI when ready to send real SMS.
/// </summary>
public sealed class TwilioSmsProvider : ISmsProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _accountSid;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(IConfiguration configuration, ILogger<TwilioSmsProvider> logger)
    {
        _logger = logger;
        _accountSid = configuration["Twilio:AccountSid"]
            ?? throw new InvalidOperationException("Twilio:AccountSid is not configured.");
        string authToken = configuration["Twilio:AuthToken"]
            ?? throw new InvalidOperationException("Twilio:AuthToken is not configured.");

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/")
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_accountSid}:{authToken}")));
    }

    public async Task<SmsResult> SendAsync(string fromPhone, string toPhone, string body, CancellationToken cancellationToken = default)
    {
        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("From", fromPhone),
            new KeyValuePair<string, string>("To", toPhone),
            new KeyValuePair<string, string>("Body", body)
        });

        try
        {
            HttpResponseMessage response = await _httpClient.PostAsync("Messages.json", formContent, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Twilio API error: {StatusCode} {Body}", response.StatusCode, responseBody);
                return new SmsResult(Success: false, ProviderMessageId: null, SegmentCount: null, ErrorMessage: responseBody);
            }

            using JsonDocument doc = JsonDocument.Parse(responseBody);
            string? sid = doc.RootElement.GetProperty("sid").GetString();
            int segments = doc.RootElement.TryGetProperty("num_segments", out JsonElement segEl)
                ? int.TryParse(segEl.GetString(), out int s) ? s : 1
                : 1;

            _logger.LogInformation("Twilio SMS sent: {Sid} → {To}", sid, toPhone);

            return new SmsResult(Success: true, ProviderMessageId: sid, SegmentCount: segments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Twilio SMS send failed to {To}", toPhone);
            return new SmsResult(Success: false, ProviderMessageId: null, SegmentCount: null, ErrorMessage: ex.Message);
        }
    }
}
