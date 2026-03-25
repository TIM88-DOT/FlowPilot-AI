using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowPilot.IntegrationTests.Fixtures;

namespace FlowPilot.IntegrationTests;

/// <summary>
/// Integration tests for messaging: consent gate, templated/raw send, inbound SMS,
/// STOP keyword opt-out, delivery status, and template CRUD.
/// </summary>
public class MessagingEndpointsTests : IClassFixture<FlowPilotApiFactory>
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

    public MessagingEndpointsTests(FlowPilotApiFactory factory)
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<AuthResponseDto> AuthenticateAsync(string email = "msg-owner@test.dev")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email, password = "Test1234!@#",
            firstName = "Ahmed", lastName = "Benali", businessName = "Salon Prestige",
            businessPhone = "+213555123456", timezone = "Africa/Algiers", defaultLanguage = "fr"
        });
        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        AuthResponseDto? auth = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return auth;
    }

    private async Task<CustomerDto> CreateCustomerAsync(string phone, string firstName = "Karim")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, new
        {
            phone, firstName, lastName = "Hadj", consentSource = "Manual"
        });
        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions))!;
    }

    private async Task OptInCustomerAsync(Guid customerId)
    {
        HttpResponseMessage http = await _client.PutAsJsonAsync($"{CustomersBase}/{customerId}/consent", new
        {
            status = "OptedIn", source = "Manual", notes = "Agreed verbally"
        });
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
    }

    /// <summary>
    /// Gets the system "appointment_reminder" template ID seeded during registration.
    /// </summary>
    private async Task<Guid> GetReminderTemplateIdAsync()
    {
        HttpResponseMessage http = await _client.GetAsync(TemplatesBase);
        List<TemplateDto>? templates = await http.Content.ReadFromJsonAsync<List<TemplateDto>>(JsonOptions);
        TemplateDto reminder = templates!.First(t => t.Name == "appointment_reminder");
        return reminder.Id;
    }

    // =====================================================================
    // CONSENT GATE
    // =====================================================================

    [Fact]
    public async Task SendTemplated_PendingConsent_Returns403()
    {
        await AuthenticateAsync(email: "consent-pending@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213670000001");
        Guid templateId = await GetReminderTemplateIdAsync();

        HttpResponseMessage http = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id,
            templateId,
            variables = new Dictionary<string, string>
            {
                ["customer_name"] = "Karim",
                ["appointment_date"] = "2026-04-01",
                ["appointment_time"] = "10:00"
            }
        });

        Assert.Equal(HttpStatusCode.Forbidden, http.StatusCode);
    }

    [Fact]
    public async Task SendTemplated_OptedOut_Returns403()
    {
        await AuthenticateAsync(email: "consent-out@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213670000002");

        // Opt in then out
        await OptInCustomerAsync(customer.Id);
        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedOut", source = "Manual"
        });

        Guid templateId = await GetReminderTemplateIdAsync();
        HttpResponseMessage http = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id,
            templateId,
            variables = new Dictionary<string, string> { ["customer_name"] = "K" }
        });

        Assert.Equal(HttpStatusCode.Forbidden, http.StatusCode);
    }

    // =====================================================================
    // TEMPLATED SEND
    // =====================================================================

    [Fact]
    public async Task SendTemplated_OptedIn_Returns200WithRenderedBody()
    {
        await AuthenticateAsync(email: "send-ok@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213670000003");
        await OptInCustomerAsync(customer.Id);
        Guid templateId = await GetReminderTemplateIdAsync();

        HttpResponseMessage http = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id,
            templateId,
            variables = new Dictionary<string, string>
            {
                ["customer_name"] = "Karim",
                ["appointment_date"] = "01/04/2026",
                ["appointment_time"] = "10:00"
            }
        });

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        SendSmsResponseDto? result = await http.Content.ReadFromJsonAsync<SendSmsResponseDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Contains("Karim", result!.RenderedBody);
        Assert.Contains("01/04/2026", result.RenderedBody);
        Assert.Contains("10:00", result.RenderedBody);
        Assert.NotNull(result.ProviderMessageId);
        Assert.StartsWith("FAKE_", result.ProviderMessageId!); // Using FakeSmsProvider
        Assert.NotEqual(Guid.Empty, result.MessageId);
    }

    // =====================================================================
    // RAW SEND
    // =====================================================================

    [Fact]
    public async Task SendRaw_OptedIn_Returns200()
    {
        await AuthenticateAsync(email: "send-raw@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213670000004");
        await OptInCustomerAsync(customer.Id);

        HttpResponseMessage http = await _client.PostAsJsonAsync($"{MessagingBase}/send-raw", new
        {
            customerId = customer.Id,
            body = "Bonjour! Votre rendez-vous est confirmé."
        });

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        SendSmsResponseDto? result = await http.Content.ReadFromJsonAsync<SendSmsResponseDto>(JsonOptions);
        Assert.Equal("Bonjour! Votre rendez-vous est confirmé.", result!.RenderedBody);
    }

    [Fact]
    public async Task SendRaw_PendingConsent_Returns403()
    {
        await AuthenticateAsync(email: "raw-pending@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213670000005");

        HttpResponseMessage http = await _client.PostAsJsonAsync($"{MessagingBase}/send-raw", new
        {
            customerId = customer.Id,
            body = "Hello"
        });

        Assert.Equal(HttpStatusCode.Forbidden, http.StatusCode);
    }

    // =====================================================================
    // INBOUND SMS
    // =====================================================================

    [Fact]
    public async Task InboundSms_ValidMessage_Returns200()
    {
        await AuthenticateAsync(email: "inbound-ok@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213670000006");

        HttpResponseMessage http = await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", new
        {
            providerSmsSid = "SM_inbound_001",
            fromPhone = "+213670000006",
            toPhone = "+10000000000",
            body = "Oui je confirme"
        });

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
    }

    [Fact]
    public async Task InboundSms_DuplicateSmsSid_IsIdempotent()
    {
        await AuthenticateAsync(email: "inbound-dup@test.dev");
        await CreateCustomerAsync("+213670000007");

        object payload = new
        {
            providerSmsSid = "SM_dup_001",
            fromPhone = "+213670000007",
            toPhone = "+10000000000",
            body = "Hello"
        };

        HttpResponseMessage http1 = await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", payload);
        HttpResponseMessage http2 = await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", payload);

        Assert.Equal(HttpStatusCode.OK, http1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, http2.StatusCode);
    }

    // =====================================================================
    // STOP KEYWORD OPT-OUT
    // =====================================================================

    [Fact]
    public async Task InboundSms_StopKeyword_OptsOutCustomer()
    {
        await AuthenticateAsync(email: "stop@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213670000008");
        await OptInCustomerAsync(customer.Id);

        // Send STOP
        await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", new
        {
            providerSmsSid = "SM_stop_001",
            fromPhone = "+213670000008",
            toPhone = "+10000000000",
            body = "STOP"
        });

        // Customer should now be OptedOut
        HttpResponseMessage getHttp = await _client.GetAsync($"{CustomersBase}/{customer.Id}");
        CustomerDto? updated = await getHttp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        Assert.Equal("OptedOut", updated!.ConsentStatus);

        // Sending SMS should now fail
        Guid templateId = await GetReminderTemplateIdAsync();
        HttpResponseMessage sendHttp = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id,
            templateId,
            variables = new Dictionary<string, string> { ["customer_name"] = "Test" }
        });
        Assert.Equal(HttpStatusCode.Forbidden, sendHttp.StatusCode);
    }

    [Fact]
    public async Task InboundSms_StopVariations_AllOptOut()
    {
        await AuthenticateAsync(email: "stop-variations@test.dev");
        string[] keywords = ["STOP", "stop", "UNSUBSCRIBE", "Cancel", "END"];

        for (int i = 0; i < keywords.Length; i++)
        {
            string phone = $"+2136700100{i:D2}";
            CustomerDto customer = await CreateCustomerAsync(phone, $"User{i}");
            await OptInCustomerAsync(customer.Id);

            await _client.PostAsJsonAsync($"{WebhooksBase}/sms/inbound", new
            {
                providerSmsSid = $"SM_stop_var_{i}",
                fromPhone = phone,
                toPhone = "+10000000000",
                body = keywords[i]
            });

            HttpResponseMessage getHttp = await _client.GetAsync($"{CustomersBase}/{customer.Id}");
            CustomerDto? updated = await getHttp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
            Assert.Equal("OptedOut", updated!.ConsentStatus);
        }
    }

    // =====================================================================
    // DELIVERY STATUS
    // =====================================================================

    [Fact]
    public async Task DeliveryStatus_UpdatesMessageStatus()
    {
        await AuthenticateAsync(email: "delivery@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+213670000009");
        await OptInCustomerAsync(customer.Id);

        // Send an SMS to get a ProviderMessageId
        HttpResponseMessage sendHttp = await _client.PostAsJsonAsync($"{MessagingBase}/send-raw", new
        {
            customerId = customer.Id,
            body = "Test delivery"
        });
        SendSmsResponseDto? sendResult = await sendHttp.Content.ReadFromJsonAsync<SendSmsResponseDto>(JsonOptions);

        // Report delivery
        HttpResponseMessage statusHttp = await _client.PostAsJsonAsync($"{WebhooksBase}/sms/status", new
        {
            providerMessageId = sendResult!.ProviderMessageId,
            status = "delivered"
        });

        Assert.Equal(HttpStatusCode.OK, statusHttp.StatusCode);
    }

    // =====================================================================
    // TEMPLATE CRUD
    // =====================================================================

    [Fact]
    public async Task Templates_ListIncludesSeededSystemTemplates()
    {
        await AuthenticateAsync(email: "tpl-list@test.dev");

        HttpResponseMessage http = await _client.GetAsync(TemplatesBase);
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        List<TemplateDto>? templates = await http.Content.ReadFromJsonAsync<List<TemplateDto>>(JsonOptions);
        Assert.NotNull(templates);
        Assert.True(templates!.Count >= 4); // reminder, confirmed, review, cancelled
        Assert.All(templates, t => Assert.True(t.IsSystem));
    }

    [Fact]
    public async Task Templates_CreateCustomTemplate_Returns201()
    {
        await AuthenticateAsync(email: "tpl-create@test.dev");

        HttpResponseMessage http = await _client.PostAsJsonAsync(TemplatesBase, new
        {
            name = "custom_promo",
            description = "Promotional SMS",
            category = "marketing",
            localeVariants = new[]
            {
                new { locale = "fr", body = "Bonjour {{customer_name}}, profitez de -20% cette semaine !" },
                new { locale = "ar", body = "مرحباً {{customer_name}}، استفد من خصم 20% هذا الأسبوع!" }
            }
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        TemplateDto? template = await http.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);
        Assert.Equal("custom_promo", template!.Name);
        Assert.False(template.IsSystem);
        Assert.Equal(2, template.LocaleVariants.Count);
    }

    [Fact]
    public async Task Templates_UpdateCustomTemplate_Returns200()
    {
        await AuthenticateAsync(email: "tpl-update@test.dev");

        // Create custom template
        HttpResponseMessage createHttp = await _client.PostAsJsonAsync(TemplatesBase, new
        {
            name = "to_update",
            description = "Will be updated",
            category = "custom",
            localeVariants = new[] { new { locale = "fr", body = "Original" } }
        });
        TemplateDto? created = await createHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);

        // Update it
        HttpResponseMessage updateHttp = await _client.PutAsJsonAsync($"{TemplatesBase}/{created!.Id}", new
        {
            name = "updated_name",
            description = "Updated description"
        });

        Assert.Equal(HttpStatusCode.OK, updateHttp.StatusCode);
        TemplateDto? updated = await updateHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);
        Assert.Equal("updated_name", updated!.Name);
        Assert.Equal("Updated description", updated.Description);
    }

    [Fact]
    public async Task Templates_CannotUpdateSystemTemplate_Returns403()
    {
        await AuthenticateAsync(email: "tpl-sys-update@test.dev");
        Guid templateId = await GetReminderTemplateIdAsync();

        HttpResponseMessage http = await _client.PutAsJsonAsync($"{TemplatesBase}/{templateId}", new
        {
            name = "hacked"
        });

        Assert.Equal(HttpStatusCode.Forbidden, http.StatusCode);
    }

    [Fact]
    public async Task Templates_CannotDeleteSystemTemplate_Returns403()
    {
        await AuthenticateAsync(email: "tpl-sys-delete@test.dev");
        Guid templateId = await GetReminderTemplateIdAsync();

        HttpResponseMessage http = await _client.DeleteAsync($"{TemplatesBase}/{templateId}");

        Assert.Equal(HttpStatusCode.Forbidden, http.StatusCode);
    }

    [Fact]
    public async Task Templates_DeleteCustomTemplate_Returns200()
    {
        await AuthenticateAsync(email: "tpl-delete@test.dev");

        HttpResponseMessage createHttp = await _client.PostAsJsonAsync(TemplatesBase, new
        {
            name = "to_delete", category = "custom",
            localeVariants = new[] { new { locale = "fr", body = "Delete me" } }
        });
        TemplateDto? created = await createHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);

        HttpResponseMessage deleteHttp = await _client.DeleteAsync($"{TemplatesBase}/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteHttp.StatusCode);

        // Should be gone (soft deleted)
        HttpResponseMessage getHttp = await _client.GetAsync($"{TemplatesBase}/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getHttp.StatusCode);
    }

    // =====================================================================
    // LOCALE VARIANT MANAGEMENT
    // =====================================================================

    [Fact]
    public async Task Templates_UpsertLocaleVariant_AddsNew()
    {
        await AuthenticateAsync(email: "tpl-variant-add@test.dev");

        HttpResponseMessage createHttp = await _client.PostAsJsonAsync(TemplatesBase, new
        {
            name = "variant_test", category = "custom",
            localeVariants = new[] { new { locale = "fr", body = "French" } }
        });
        TemplateDto? created = await createHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);

        // Add Arabic variant
        HttpResponseMessage upsertHttp = await _client.PutAsJsonAsync($"{TemplatesBase}/{created!.Id}/variants", new
        {
            locale = "ar", body = "Arabic text"
        });

        Assert.Equal(HttpStatusCode.OK, upsertHttp.StatusCode);
        TemplateDto? updated = await upsertHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);
        Assert.Equal(2, updated!.LocaleVariants.Count);
    }

    [Fact]
    public async Task Templates_UpsertLocaleVariant_UpdatesExisting()
    {
        await AuthenticateAsync(email: "tpl-variant-upd@test.dev");

        HttpResponseMessage createHttp = await _client.PostAsJsonAsync(TemplatesBase, new
        {
            name = "variant_update", category = "custom",
            localeVariants = new[] { new { locale = "fr", body = "Original French" } }
        });
        TemplateDto? created = await createHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);

        // Update the French variant
        HttpResponseMessage upsertHttp = await _client.PutAsJsonAsync($"{TemplatesBase}/{created!.Id}/variants", new
        {
            locale = "fr", body = "Updated French"
        });

        TemplateDto? updated = await upsertHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);
        Assert.Single(updated!.LocaleVariants);
        Assert.Equal("Updated French", updated.LocaleVariants[0].Body);
    }

    [Fact]
    public async Task Templates_DeleteLocaleVariant_Removes()
    {
        await AuthenticateAsync(email: "tpl-variant-del@test.dev");

        HttpResponseMessage createHttp = await _client.PostAsJsonAsync(TemplatesBase, new
        {
            name = "variant_del", category = "custom",
            localeVariants = new[]
            {
                new { locale = "fr", body = "French" },
                new { locale = "ar", body = "Arabic" }
            }
        });
        TemplateDto? created = await createHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);

        HttpResponseMessage delHttp = await _client.DeleteAsync($"{TemplatesBase}/{created!.Id}/variants/ar");
        Assert.Equal(HttpStatusCode.OK, delHttp.StatusCode);

        // Verify only French remains
        HttpResponseMessage getHttp = await _client.GetAsync($"{TemplatesBase}/{created.Id}");
        TemplateDto? updated = await getHttp.Content.ReadFromJsonAsync<TemplateDto>(JsonOptions);
        Assert.Single(updated!.LocaleVariants);
        Assert.Equal("fr", updated.LocaleVariants[0].Locale);
    }

    // =====================================================================
    // TEMPLATE RENDERING WITH LOCALE FALLBACK
    // =====================================================================

    [Fact]
    public async Task SendTemplated_UsesCustomerPreferredLanguage()
    {
        await AuthenticateAsync(email: "locale-match@test.dev");

        // Create customer with Arabic preference
        HttpResponseMessage custHttp = await _client.PostAsJsonAsync(CustomersBase, new
        {
            phone = "+213670000020",
            firstName = "فاطمة",
            preferredLanguage = "ar",
            consentSource = "Manual"
        });
        CustomerDto? customer = await custHttp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        await OptInCustomerAsync(customer!.Id);

        Guid templateId = await GetReminderTemplateIdAsync();

        HttpResponseMessage sendHttp = await _client.PostAsJsonAsync($"{MessagingBase}/send", new
        {
            customerId = customer.Id,
            templateId,
            variables = new Dictionary<string, string>
            {
                ["customer_name"] = "فاطمة",
                ["appointment_date"] = "01/04/2026",
                ["appointment_time"] = "14:00"
            }
        });

        Assert.Equal(HttpStatusCode.OK, sendHttp.StatusCode);
        SendSmsResponseDto? result = await sendHttp.Content.ReadFromJsonAsync<SendSmsResponseDto>(JsonOptions);
        // Arabic template contains "مرحباً" (hello in Arabic)
        Assert.Contains("مرحباً", result!.RenderedBody);
        Assert.Contains("فاطمة", result.RenderedBody);
    }
}
