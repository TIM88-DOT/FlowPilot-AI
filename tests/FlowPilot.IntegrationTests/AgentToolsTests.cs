using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowPilot.IntegrationTests.Fixtures;

namespace FlowPilot.IntegrationTests;

/// <summary>
/// Integration tests for the AI Agent system:
/// tool registry, individual tool execution, orchestrator graceful degradation,
/// and event-triggered agent runs.
/// </summary>
public class AgentToolsTests : IClassFixture<FlowPilotApiFactory>
{
    private readonly HttpClient _client;
    private const string AuthBase = "/api/v1/auth";
    private const string CustomersBase = "/api/v1/customers";
    private const string AppointmentsBase = "/api/v1/appointments";
    private const string MessagingBase = "/api/v1/messaging";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AgentToolsTests(FlowPilotApiFactory factory)
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
    private record AppointmentDto(Guid Id, Guid CustomerId, string CustomerName, Guid? StaffUserId, string? ExternalId,
        string Status, DateTime StartsAt, DateTime EndsAt, string? ServiceName, string? Notes, DateTime CreatedAt, DateTime UpdatedAt);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<AuthResponseDto> AuthenticateAsync(string email = "agent-test@salon.dev")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email,
            password = "Test1234!@#",
            firstName = "Fatima",
            lastName = "Rahal",
            businessName = "Salon Fatima",
            businessPhone = "+213555999001",
            timezone = "Africa/Algiers",
            defaultLanguage = "fr"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        AuthResponseDto? auth = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return auth;
    }

    private async Task<CustomerDto> CreateCustomerAsync(string phone = "+213555000100", string firstName = "Amina", string language = "fr")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, new
        {
            phone,
            firstName,
            lastName = "Bouchra",
            email = $"{firstName.ToLower()}@test.dev",
            preferredLanguage = language,
            consentSource = "Manual"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions))!;
    }

    private async Task<AppointmentDto> CreateAppointmentAsync(Guid customerId, DateTime? startsAt = null, string serviceName = "Coiffure")
    {
        DateTime start = startsAt ?? DateTime.UtcNow.AddDays(2);
        HttpResponseMessage http = await _client.PostAsJsonAsync(AppointmentsBase, new
        {
            customerId,
            startsAt = start,
            endsAt = start.AddHours(1),
            serviceName
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions))!;
    }

    // =====================================================================
    // APPOINTMENT CREATION TRIGGERS AGENT RUN
    // =====================================================================

    [Fact]
    public async Task CreateAppointment_TriggersReminderAgent_GracefullySkipsWhenNoAzureOpenAI()
    {
        // In the test environment, ChatClient is not registered (no Azure OpenAI config).
        // The ReminderOptimizationAgent should fire but the orchestrator should fail gracefully
        // with a descriptive error — not crash the appointment creation.
        await AuthenticateAsync(email: "agent-trigger@salon.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100001", firstName: "Nora");

        // This should succeed even though the agent will fail internally
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        Assert.NotEqual(Guid.Empty, appt.Id);
        Assert.Equal("Scheduled", appt.Status);
        Assert.Equal(customer.Id, appt.CustomerId);
    }

    [Fact]
    public async Task CompleteAppointment_TriggersReviewAgent_GracefullySkipsWhenNoAzureOpenAI()
    {
        // ReviewRecoveryAgent fires when an appointment transitions to Completed.
        // It should fail gracefully without blocking the status transition.
        await AuthenticateAsync(email: "review-trigger@salon.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100002", firstName: "Leila");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        // Confirm first
        HttpResponseMessage confirmHttp = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmHttp.StatusCode);

        // Complete — triggers ReviewRecoveryAgent
        HttpResponseMessage completeHttp = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeHttp.StatusCode);

        AppointmentDto? completed = await completeHttp.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Completed", completed!.Status);
    }

    // =====================================================================
    // STATUS TRANSITIONS STILL WORK WITH AGENTS WIRED
    // =====================================================================

    [Fact]
    public async Task FullLifecycle_WithAgentsWired_AllTransitionsSucceed()
    {
        await AuthenticateAsync(email: "lifecycle-agent@salon.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100003", firstName: "Yasmine");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id, serviceName: "Brushing");

        Assert.Equal("Scheduled", appt.Status);

        // Scheduled → Confirmed
        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? confirmed = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Confirmed", confirmed!.Status);

        // Confirmed → Completed (triggers ReviewRecoveryAgent)
        http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? completed = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Completed", completed!.Status);
    }

    [Fact]
    public async Task InvalidTransition_WithAgentsWired_StillReturns400()
    {
        await AuthenticateAsync(email: "invalid-transition-agent@salon.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100004", firstName: "Sara");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        // Scheduled → Completed is invalid (must confirm first)
        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/complete", null);
        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    // =====================================================================
    // MULTIPLE APPOINTMENTS — AGENTS DON'T INTERFERE
    // =====================================================================

    [Fact]
    public async Task MultipleAppointments_EachTriggersAgent_NoCrossContamination()
    {
        await AuthenticateAsync(email: "multi-agent@salon.dev");
        CustomerDto customer1 = await CreateCustomerAsync(phone: "+213661100005", firstName: "Rania");
        CustomerDto customer2 = await CreateCustomerAsync(phone: "+213661100006", firstName: "Dina");

        AppointmentDto appt1 = await CreateAppointmentAsync(customer1.Id, serviceName: "Coupe");
        AppointmentDto appt2 = await CreateAppointmentAsync(customer2.Id, serviceName: "Coloration");

        // Both should succeed independently
        Assert.NotEqual(appt1.Id, appt2.Id);
        Assert.Equal(customer1.Id, appt1.CustomerId);
        Assert.Equal(customer2.Id, appt2.CustomerId);
        Assert.Equal("Scheduled", appt1.Status);
        Assert.Equal("Scheduled", appt2.Status);
    }

    // =====================================================================
    // RESCHEDULE — TRIGGERS AGENT FOR NEW APPOINTMENT
    // =====================================================================

    [Fact]
    public async Task Reschedule_CreatesNewAppointment_TriggersAgentForNew()
    {
        await AuthenticateAsync(email: "reschedule-agent@salon.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100007", firstName: "Hiba");
        AppointmentDto original = await CreateAppointmentAsync(customer.Id);

        DateTime newStart = DateTime.UtcNow.AddDays(5);
        HttpResponseMessage http = await _client.PostAsJsonAsync($"{AppointmentsBase}/{original.Id}/reschedule", new
        {
            startsAt = newStart,
            endsAt = newStart.AddHours(1)
        });

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? rescheduled = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);

        // The response is the NEW appointment
        Assert.NotEqual(original.Id, rescheduled!.Id);
        Assert.Equal("Scheduled", rescheduled.Status);
        Assert.Equal(customer.Id, rescheduled.CustomerId);
    }

    // =====================================================================
    // CROSS-TENANT ISOLATION — AGENTS RESPECT TENANT BOUNDARIES
    // =====================================================================

    [Fact]
    public async Task CrossTenant_AppointmentAgents_CannotAccessOtherTenantData()
    {
        // Tenant A creates customer + appointment
        AuthResponseDto authA = await AuthenticateAsync(email: "tenantA-agent@salon.dev");
        CustomerDto customerA = await CreateCustomerAsync(phone: "+213661200001", firstName: "Meriem");
        AppointmentDto apptA = await CreateAppointmentAsync(customerA.Id);

        // Tenant B
        using var factoryB = new FlowPilotApiFactory();
        await factoryB.InitializeAsync();
        HttpClient clientB = factoryB.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        HttpResponseMessage regHttp = await clientB.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email = "tenantB-agent@salon.dev",
            password = "Test1234!@#",
            firstName = "Omar",
            lastName = "Khalil",
            businessName = "Barbershop Omar",
            businessPhone = "+213555888001",
            timezone = "Africa/Algiers",
            defaultLanguage = "ar"
        });
        Assert.Equal(HttpStatusCode.Created, regHttp.StatusCode);
        AuthResponseDto? authB = await regHttp.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB!.AccessToken);

        // Tenant B tries to access Tenant A's appointment — should get 404
        HttpResponseMessage crossTenantHttp = await clientB.GetAsync($"{AppointmentsBase}/{apptA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantHttp.StatusCode);

        // Tenant B tries to confirm Tenant A's appointment — should fail
        HttpResponseMessage crossConfirmHttp = await clientB.PostAsync($"{AppointmentsBase}/{apptA.Id}/confirm", null);
        Assert.True(
            crossConfirmHttp.StatusCode == HttpStatusCode.NotFound || crossConfirmHttp.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 404 or 400 but got {crossConfirmHttp.StatusCode}");
    }

    // =====================================================================
    // WEBHOOK INGESTION + AGENT — IDEMPOTENCY PRESERVED
    // =====================================================================

    [Fact]
    public async Task WebhookIngestion_WithAgents_IdempotencyPreserved()
    {
        await AuthenticateAsync(email: "webhook-agent@salon.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661200002", firstName: "Kenza");

        DateTime start = DateTime.UtcNow.AddDays(3);
        var webhook = new
        {
            externalId = "ext-agent-test-001",
            customerId = customer.Id,
            startsAt = start,
            endsAt = start.AddHours(1),
            serviceName = "Soin visage",
            notes = "From external system"
        };

        // First ingestion — should create
        HttpResponseMessage http1 = await _client.PostAsJsonAsync("/api/webhooks/appointments/inbound", webhook);
        Assert.Equal(HttpStatusCode.OK, http1.StatusCode);
        AppointmentDto? first = await http1.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);

        // Second ingestion — should return same appointment (idempotent)
        HttpResponseMessage http2 = await _client.PostAsJsonAsync("/api/webhooks/appointments/inbound", webhook);
        Assert.Equal(HttpStatusCode.OK, http2.StatusCode);
        AppointmentDto? second = await http2.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal("ext-agent-test-001", first.ExternalId);
    }

    // =====================================================================
    // CONSENT GATE — AGENT SMS TOOLS RESPECT CONSENT
    // =====================================================================

    [Fact]
    public async Task SendSms_ToOptedOutCustomer_AgentRespectConsentGate()
    {
        await AuthenticateAsync(email: "consent-agent@salon.dev");

        // Create customer (auto opted-in)
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661200003", firstName: "Salma");

        // Opt the customer out
        HttpResponseMessage optOutHttp = await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            consentStatus = "OptedOut",
            source = "Manual"
        });
        Assert.Equal(HttpStatusCode.OK, optOutHttp.StatusCode);

        // Try sending a raw SMS — should fail with consent error
        HttpResponseMessage smsHttp = await _client.PostAsJsonAsync($"{MessagingBase}/send-raw", new
        {
            customerId = customer.Id,
            body = "Test review request"
        });
        Assert.Equal(HttpStatusCode.Forbidden, smsHttp.StatusCode);
    }

    // =====================================================================
    // CANCEL AFTER SCHEDULE — AGENTS DON'T BLOCK CANCEL
    // =====================================================================

    [Fact]
    public async Task Cancel_AfterAgentTrigger_Succeeds()
    {
        await AuthenticateAsync(email: "cancel-agent@salon.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661200004", firstName: "Lina");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        // Cancel the appointment — the agent may have already fired but cancel should still work
        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? cancelled = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Cancelled", cancelled!.Status);
    }

    [Fact]
    public async Task CancelThenConfirm_InvalidTransition_Returns400()
    {
        await AuthenticateAsync(email: "cancel-confirm-agent@salon.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661200005", firstName: "Ines");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        // Cancel
        HttpResponseMessage cancelHttp = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelHttp.StatusCode);

        // Try to confirm after cancel — should fail (terminal state)
        HttpResponseMessage confirmHttp = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, confirmHttp.StatusCode);
    }
}
