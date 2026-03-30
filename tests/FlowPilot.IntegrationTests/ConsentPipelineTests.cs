using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowPilot.IntegrationTests.Fixtures;

namespace FlowPilot.IntegrationTests;

/// <summary>
/// End-to-end consent pipeline tests: opt-in → send → STOP keyword → blocked → re-opt-in → send again.
/// Verifies the full lifecycle of consent management across messaging and webhooks.
/// </summary>
public class ConsentPipelineTests : IClassFixture<FlowPilotApiFactory>
{
    private readonly HttpClient _client;
    private const string AuthBase = "/api/v1/auth";
    private const string CustomersBase = "/api/v1/customers";
    private const string MessagingBase = "/api/v1/messaging";
    private const string TemplatesBase = "/api/v1/templates";
    private const string WebhooksBase = "/api/webhooks";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ConsentPipelineTests(FlowPilotApiFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
    }

    // -----------------------------------------------------------------------
    // DTOs
    // -----------------------------------------------------------------------

    private record AuthResponseDto(string AccessToken, DateTime ExpiresAt, UserInfoDto User);
    private record UserInfoDto(Guid Id, Guid TenantId, string Email, string FirstName, string LastName, string Role, string BusinessName);
    private record CustomerDto(Guid Id, string Phone, string? Email, string FirstName, string? LastName,
        string PreferredLanguage, string? Tags, decimal NoShowScore, string ConsentStatus, DateTime CreatedAt, DateTime UpdatedAt);
    private record SendSmsResponseDto(Guid MessageId, string? ProviderMessageId, string RenderedBody, int? SegmentCount);
    private record TemplateDto(Guid Id, string Name, string? Description, string Category, bool IsSystem,
        List<LocaleVariantDto> LocaleVariants, DateTime CreatedAt, DateTime UpdatedAt);
    private record LocaleVariantDto(Guid Id, string Locale, string Body, int SegmentCount);
    private record ConsentHistoryDto(Guid Id, string Status, string Source, string? Notes, DateTime CreatedAt);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<AuthResponseDto> AuthenticateAsync(string email)
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email, password = "Test1234!@#",
            firstName = "Owner", lastName = "Test", businessName = "Salon Test",
            businessPhone = "+213555000000", timezone = "Africa/Algiers", defaultLanguage = "fr"
        });
        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        AuthResponseDto? auth = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return auth;
    }

    private async Task<CustomerDto> CreateCustomerAsync(string phone, string firstName = "Client")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, new
        {
            phone, firstName, lastName = "Test", consentSource = "Manual"
        });
        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions))!;
    }

    private async Task<Guid> GetReminderTemplateIdAsync()
    {
        HttpResponseMessage http = await _client.GetAsync(TemplatesBase);
        List<TemplateDto>? templates = await http.Content.ReadFromJsonAsync<List<TemplateDto>>(JsonOptions);
        return templates!.First(t => t.Name == "appointment_reminder").Id;
    }

    private async Task<CustomerDto> GetCustomerAsync(Guid id)
    {
        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}/{id}");
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions))!;
    }

    // =====================================================================
    // FULL CONSENT LIFECYCLE
    // =====================================================================

    [Fact]
    public async Task FullPipeline_OptIn_Send_StopKeyword_Blocked_ReOptIn_SendAgain()
    {
        await AuthenticateAsync("consent-full@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213780000001", "FullPipeline");
        Guid templateId = await GetReminderTemplateIdAsync();

        var variables = new Dictionary<string, string>
        {
            ["customer_name"] = "FullPipeline",
            ["appointment_date"] = "2026-04-15",
            ["appointment_time"] = "14:00"
        };

        // 1. New customer has Pending consent — SMS should be blocked
        Assert.Equal("Pending", customer.ConsentStatus);
        HttpResponseMessage send1 = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id, templateId, variables
        });
        Assert.Equal(HttpStatusCode.Forbidden, send1.StatusCode);

        // 2. Opt in — SMS should succeed
        HttpResponseMessage optIn = await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedIn", source = "Manual", notes = "Verbal consent"
        });
        Assert.Equal(HttpStatusCode.OK, optIn.StatusCode);

        CustomerDto afterOptIn = await GetCustomerAsync(customer.Id);
        Assert.Equal("OptedIn", afterOptIn.ConsentStatus);

        HttpResponseMessage send2 = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id, templateId, variables
        });
        Assert.Equal(HttpStatusCode.OK, send2.StatusCode);

        // 3. Customer sends STOP via inbound SMS — consent becomes OptedOut
        await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", new
        {
            providerSmsSid = "SM_pipeline_stop_001",
            fromPhone = "+213780000001",
            toPhone = "+10000000000",
            body = "STOP"
        });

        CustomerDto afterStop = await GetCustomerAsync(customer.Id);
        Assert.Equal("OptedOut", afterStop.ConsentStatus);

        // 4. SMS should now be blocked
        HttpResponseMessage send3 = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id, templateId, variables
        });
        Assert.Equal(HttpStatusCode.Forbidden, send3.StatusCode);

        // 5. Re-opt-in manually — SMS should succeed again
        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedIn", source = "Manual", notes = "Customer called back to re-subscribe"
        });

        CustomerDto afterReOptIn = await GetCustomerAsync(customer.Id);
        Assert.Equal("OptedIn", afterReOptIn.ConsentStatus);

        HttpResponseMessage send4 = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id, templateId, variables
        });
        Assert.Equal(HttpStatusCode.OK, send4.StatusCode);
    }

    // =====================================================================
    // CONSENT HISTORY TRACKING
    // =====================================================================

    [Fact]
    public async Task ConsentChanges_AreTrackedInHistory()
    {
        await AuthenticateAsync("consent-history@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213780000002", "HistoryCheck");

        // Opt in
        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedIn", source = "Manual", notes = "Signed form"
        });

        // Opt out
        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedOut", source = "Manual", notes = "Requested by phone"
        });

        // Check history
        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}/{customer.Id}/history");
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);

        List<ConsentHistoryDto>? history = await http.Content.ReadFromJsonAsync<List<ConsentHistoryDto>>(JsonOptions);
        Assert.NotNull(history);
        Assert.True(history!.Count >= 2, $"Expected at least 2 consent changes, got {history.Count}");

        // History should contain both the OptedIn and OptedOut records
        Assert.Contains(history, h => h.Status == "OptedIn");
        Assert.Contains(history, h => h.Status == "OptedOut");
    }

    // =====================================================================
    // RAW SMS CONSENT GATE
    // =====================================================================

    [Fact]
    public async Task SendRaw_PendingConsent_Returns403()
    {
        await AuthenticateAsync("consent-raw-pending@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213780000003", "RawPending");

        HttpResponseMessage http = await _client.PostAsJsonAsync($"{MessagingBase}/send-raw", new
        {
            customerId = customer.Id,
            body = "Test raw SMS"
        });
        Assert.Equal(HttpStatusCode.Forbidden, http.StatusCode);
    }

    [Fact]
    public async Task SendRaw_OptedIn_Succeeds()
    {
        await AuthenticateAsync("consent-raw-optin@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213780000004", "RawOptedIn");

        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedIn", source = "Manual"
        });

        HttpResponseMessage http = await _client.PostAsJsonAsync($"{MessagingBase}/send-raw", new
        {
            customerId = customer.Id,
            body = "You have an appointment tomorrow"
        });
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
    }

    // =====================================================================
    // STOP KEYWORD EDGE CASES
    // =====================================================================

    [Fact]
    public async Task StopKeyword_WithExtraWhitespace_StillOptsOut()
    {
        await AuthenticateAsync("consent-stop-ws@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213780000005", "WhitespaceStop");

        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedIn", source = "Manual"
        });

        await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", new
        {
            providerSmsSid = "SM_ws_stop_001",
            fromPhone = "+213780000005",
            toPhone = "+10000000000",
            body = "  STOP  "
        });

        CustomerDto updated = await GetCustomerAsync(customer.Id);
        Assert.Equal("OptedOut", updated.ConsentStatus);
    }

    [Fact]
    public async Task StopKeyword_MixedCase_StillOptsOut()
    {
        await AuthenticateAsync("consent-stop-case@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213780000006", "MixedCaseStop");

        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedIn", source = "Manual"
        });

        await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", new
        {
            providerSmsSid = "SM_mixed_stop_001",
            fromPhone = "+213780000006",
            toPhone = "+10000000000",
            body = "sToP"
        });

        CustomerDto updated = await GetCustomerAsync(customer.Id);
        Assert.Equal("OptedOut", updated.ConsentStatus);
    }

    [Fact]
    public async Task NonStopMessage_DoesNotOptOut()
    {
        await AuthenticateAsync("consent-non-stop@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213780000007", "NonStopMsg");

        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedIn", source = "Manual"
        });

        await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", new
        {
            providerSmsSid = "SM_normal_001",
            fromPhone = "+213780000007",
            toPhone = "+10000000000",
            body = "OUI, I confirm my appointment"
        });

        CustomerDto updated = await GetCustomerAsync(customer.Id);
        Assert.Equal("OptedIn", updated.ConsentStatus);
    }
}
