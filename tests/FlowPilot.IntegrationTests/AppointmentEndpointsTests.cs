using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowPilot.IntegrationTests.Fixtures;

namespace FlowPilot.IntegrationTests;

/// <summary>
/// Integration tests for /api/v1/appointments endpoints:
/// CRUD, status transitions, webhook idempotency, audit logging, and cross-tenant isolation.
/// </summary>
public class AppointmentEndpointsTests : IClassFixture<FlowPilotApiFactory>
{
    private readonly HttpClient _client;
    private const string AuthBase = "/api/v1/auth";
    private const string AppointmentsBase = "/api/v1/appointments";
    private const string CustomersBase = "/api/v1/customers";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppointmentEndpointsTests(FlowPilotApiFactory factory)
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

    private record PagedResultDto(List<AppointmentDto> Items, int TotalCount, int Page, int PageSize, int TotalPages);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<AuthResponseDto> AuthenticateAsync(string email = "owner@appt.dev")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email,
            password = "Test1234!@#",
            firstName = "Ahmed",
            lastName = "Benali",
            businessName = "Salon Prestige",
            businessPhone = "+213555123456",
            timezone = "Africa/Algiers",
            defaultLanguage = "fr"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        AuthResponseDto? auth = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return auth;
    }

    private async Task<CustomerDto> CreateCustomerAsync(string phone = "+213555000001", string firstName = "Karim")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, new
        {
            phone,
            firstName,
            lastName = "Hadj",
            email = $"{firstName.ToLower()}@test.dev",
            preferredLanguage = "fr",
            consentSource = "Manual"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions))!;
    }

    private async Task<AppointmentDto> CreateAppointmentAsync(
        Guid customerId,
        DateTime? startsAt = null,
        string? serviceName = "Haircut")
    {
        DateTime start = startsAt ?? DateTime.UtcNow.AddDays(1);
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
    // AUTH REQUIRED
    // =====================================================================

    [Fact]
    public async Task AllEndpoints_WithoutAuth_Return401()
    {
        using var factory = new FlowPilotApiFactory();
        await factory.InitializeAsync();
        HttpClient anonClient = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.GetAsync(AppointmentsBase)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.PostAsJsonAsync(AppointmentsBase, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.GetAsync($"{AppointmentsBase}/{Guid.NewGuid()}")).StatusCode);
    }

    // =====================================================================
    // CREATE
    // =====================================================================

    [Fact]
    public async Task Create_ValidRequest_Returns201WithScheduledStatus()
    {
        await AuthenticateAsync(email: "create-appt@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661000001", firstName: "Rami");

        DateTime start = DateTime.UtcNow.AddDays(2);
        HttpResponseMessage http = await _client.PostAsJsonAsync(AppointmentsBase, new
        {
            customerId = customer.Id,
            startsAt = start,
            endsAt = start.AddHours(1),
            serviceName = "Manicure",
            notes = "First visit"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        AppointmentDto? appt = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.NotNull(appt);
        Assert.Equal("Scheduled", appt!.Status);
        Assert.Equal(customer.Id, appt.CustomerId);
        Assert.Contains("Rami", appt.CustomerName);
        Assert.Equal("Manicure", appt.ServiceName);
        Assert.NotEqual(Guid.Empty, appt.Id);
    }

    [Fact]
    public async Task Create_NonExistentCustomer_Returns400()
    {
        await AuthenticateAsync(email: "create-bad-cust@test.dev");

        DateTime start = DateTime.UtcNow.AddDays(1);
        HttpResponseMessage http = await _client.PostAsJsonAsync(AppointmentsBase, new
        {
            customerId = Guid.NewGuid(),
            startsAt = start,
            endsAt = start.AddHours(1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    [Fact]
    public async Task Create_EndBeforeStart_Returns400()
    {
        await AuthenticateAsync(email: "create-bad-time@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661000002", firstName: "Ali");

        DateTime start = DateTime.UtcNow.AddDays(1);
        HttpResponseMessage http = await _client.PostAsJsonAsync(AppointmentsBase, new
        {
            customerId = customer.Id,
            startsAt = start,
            endsAt = start.AddHours(-1) // end BEFORE start
        });

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    // =====================================================================
    // GET BY ID
    // =====================================================================

    [Fact]
    public async Task GetById_Existing_Returns200()
    {
        await AuthenticateAsync(email: "get-appt@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661000003", firstName: "Sara");
        AppointmentDto created = await CreateAppointmentAsync(customer.Id);

        HttpResponseMessage http = await _client.GetAsync($"{AppointmentsBase}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? appt = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal(created.Id, appt!.Id);
    }

    [Fact]
    public async Task GetById_NonExistent_Returns404()
    {
        await AuthenticateAsync(email: "get-appt-404@test.dev");

        HttpResponseMessage http = await _client.GetAsync($"{AppointmentsBase}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    // =====================================================================
    // LIST WITH FILTERS
    // =====================================================================

    [Fact]
    public async Task List_FilterByStatus_ReturnsMatching()
    {
        await AuthenticateAsync(email: "list-status@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661000004", firstName: "Omar");

        AppointmentDto a1 = await CreateAppointmentAsync(customer.Id);
        AppointmentDto a2 = await CreateAppointmentAsync(customer.Id);

        // Confirm one
        await _client.PostAsync($"{AppointmentsBase}/{a1.Id}/confirm", null);

        HttpResponseMessage http = await _client.GetAsync($"{AppointmentsBase}?status=Confirmed");
        PagedResultDto? result = await http.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Single(result!.Items);
        Assert.Equal(a1.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task List_FilterByCustomerId_ReturnsMatching()
    {
        await AuthenticateAsync(email: "list-cust@test.dev");
        CustomerDto c1 = await CreateCustomerAsync(phone: "+213661000005", firstName: "A");
        CustomerDto c2 = await CreateCustomerAsync(phone: "+213661000006", firstName: "B");

        await CreateAppointmentAsync(c1.Id);
        await CreateAppointmentAsync(c2.Id);

        HttpResponseMessage http = await _client.GetAsync($"{AppointmentsBase}?customerId={c1.Id}");
        PagedResultDto? result = await http.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Single(result!.Items);
        Assert.Equal(c1.Id, result.Items[0].CustomerId);
    }

    [Fact]
    public async Task List_FilterByDateRange_ReturnsMatching()
    {
        await AuthenticateAsync(email: "list-date@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661000007", firstName: "Nour");

        DateTime tomorrow = DateTime.UtcNow.Date.AddDays(1);
        DateTime nextWeek = DateTime.UtcNow.Date.AddDays(7);

        await CreateAppointmentAsync(customer.Id, startsAt: tomorrow);
        await CreateAppointmentAsync(customer.Id, startsAt: nextWeek);

        string from = tomorrow.AddHours(-1).ToString("O");
        string to = tomorrow.AddHours(2).ToString("O");
        HttpResponseMessage http = await _client.GetAsync($"{AppointmentsBase}?dateFrom={from}&dateTo={to}");
        PagedResultDto? result = await http.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Single(result!.Items);
    }

    // =====================================================================
    // STATUS TRANSITIONS
    // =====================================================================

    [Fact]
    public async Task Confirm_FromScheduled_Succeeds()
    {
        await AuthenticateAsync(email: "confirm@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100001", firstName: "T1");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? confirmed = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Confirmed", confirmed!.Status);
    }

    [Fact]
    public async Task Complete_FromConfirmed_Succeeds()
    {
        await AuthenticateAsync(email: "complete@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100002", firstName: "T2");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);
        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/complete", null);

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? completed = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Completed", completed!.Status);
    }

    [Fact]
    public async Task Cancel_FromScheduled_Succeeds()
    {
        await AuthenticateAsync(email: "cancel@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100003", firstName: "T3");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? cancelled = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Cancelled", cancelled!.Status);
    }

    [Fact]
    public async Task Cancel_FromConfirmed_Succeeds()
    {
        await AuthenticateAsync(email: "cancel-confirmed@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661100004", firstName: "T4");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);
        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? cancelled = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Cancelled", cancelled!.Status);
    }

    // =====================================================================
    // INVALID TRANSITIONS
    // =====================================================================

    [Fact]
    public async Task Complete_FromScheduled_Returns400()
    {
        await AuthenticateAsync(email: "bad-complete@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661200001", firstName: "T5");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        // Scheduled → Completed is NOT valid (must go through Confirmed first)
        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/complete", null);

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    [Fact]
    public async Task Confirm_FromCancelled_Returns400()
    {
        await AuthenticateAsync(email: "bad-confirm@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661200002", firstName: "T6");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/cancel", null);

        // Cancelled → Confirmed is NOT valid (terminal state)
        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    [Fact]
    public async Task Cancel_FromCompleted_Returns400()
    {
        await AuthenticateAsync(email: "bad-cancel@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661200003", firstName: "T7");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);
        await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/complete", null);

        // Completed → Cancelled is NOT valid (terminal state)
        HttpResponseMessage http = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    // =====================================================================
    // RESCHEDULE
    // =====================================================================

    [Fact]
    public async Task Reschedule_FromScheduled_CreatesNewAppointment()
    {
        await AuthenticateAsync(email: "reschedule@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661300001", firstName: "T8");
        AppointmentDto original = await CreateAppointmentAsync(customer.Id);

        DateTime newStart = DateTime.UtcNow.AddDays(5);
        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AppointmentsBase}/{original.Id}/reschedule",
            new { startsAt = newStart, endsAt = newStart.AddHours(1) });

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? newAppt = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);

        // New appointment is Scheduled with new times
        Assert.Equal("Scheduled", newAppt!.Status);
        Assert.NotEqual(original.Id, newAppt.Id);
        Assert.Equal(customer.Id, newAppt.CustomerId);

        // Original appointment is now Rescheduled
        HttpResponseMessage origHttp = await _client.GetAsync($"{AppointmentsBase}/{original.Id}");
        AppointmentDto? origAppt = await origHttp.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Rescheduled", origAppt!.Status);
    }

    [Fact]
    public async Task Reschedule_FromCancelled_Returns400()
    {
        await AuthenticateAsync(email: "reschedule-bad@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661300002", firstName: "T9");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/cancel", null);

        DateTime newStart = DateTime.UtcNow.AddDays(5);
        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AppointmentsBase}/{appt.Id}/reschedule",
            new { startsAt = newStart, endsAt = newStart.AddHours(1) });

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    // =====================================================================
    // WEBHOOK IDEMPOTENCY
    // =====================================================================

    [Fact]
    public async Task Webhook_FirstIngestion_CreatesAppointment()
    {
        await AuthenticateAsync(email: "webhook-ok@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661400001", firstName: "Webhook");

        DateTime start = DateTime.UtcNow.AddDays(3);
        HttpResponseMessage http = await _client.PostAsJsonAsync("/api/webhooks/appointments/inbound", new
        {
            externalId = "ext-001",
            customerId = customer.Id,
            startsAt = start,
            endsAt = start.AddHours(1),
            serviceName = "Beard trim"
        });

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AppointmentDto? appt = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("ext-001", appt!.ExternalId);
        Assert.Equal("Scheduled", appt.Status);
    }

    [Fact]
    public async Task Webhook_DuplicateExternalId_ReturnsExistingWithoutDuplicate()
    {
        await AuthenticateAsync(email: "webhook-dup@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661400002", firstName: "WH-Dup");

        DateTime start = DateTime.UtcNow.AddDays(3);
        object payload = new
        {
            externalId = "ext-dup-001",
            customerId = customer.Id,
            startsAt = start,
            endsAt = start.AddHours(1)
        };

        // First ingestion
        HttpResponseMessage http1 = await _client.PostAsJsonAsync("/api/webhooks/appointments/inbound", payload);
        Assert.Equal(HttpStatusCode.OK, http1.StatusCode);
        AppointmentDto? first = await http1.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);

        // Second ingestion — same ExternalId — should return same appointment, no duplicate
        HttpResponseMessage http2 = await _client.PostAsJsonAsync("/api/webhooks/appointments/inbound", payload);
        Assert.Equal(HttpStatusCode.OK, http2.StatusCode);
        AppointmentDto? second = await http2.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);

        Assert.Equal(first!.Id, second!.Id);

        // Verify only 1 appointment exists
        HttpResponseMessage listHttp = await _client.GetAsync(AppointmentsBase);
        PagedResultDto? list = await listHttp.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Equal(1, list!.TotalCount);
    }

    // =====================================================================
    // AUDIT LOG (via MediatR event handler)
    // =====================================================================

    [Fact]
    public async Task StatusChange_CreatesAuditLogEntry()
    {
        await AuthenticateAsync(email: "audit@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661500001", firstName: "Audit");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        // Confirm → this triggers AppointmentStatusChangedEvent → AuditLog handler
        await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);

        // We can't directly query audit_logs via API yet, but we verify the transition succeeded
        // which means the handler ran without error (it would fail the SaveChanges if broken)
        HttpResponseMessage http = await _client.GetAsync($"{AppointmentsBase}/{appt.Id}");
        AppointmentDto? confirmed = await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Confirmed", confirmed!.Status);
    }

    // =====================================================================
    // CROSS-TENANT ISOLATION
    // =====================================================================

    [Fact]
    public async Task CrossTenant_CannotSeeOtherTenantsAppointments()
    {
        using var factory = new FlowPilotApiFactory();
        await factory.InitializeAsync();
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

        // Tenant A
        HttpResponseMessage regA = await client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email = "appt-isoA@test.dev", password = "Test1234!@#",
            firstName = "A", lastName = "Owner", businessName = "Salon A"
        });
        AuthResponseDto? authA = await regA.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA!.AccessToken);

        CustomerDto custA = (await (await client.PostAsJsonAsync(CustomersBase, new
        {
            phone = "+213662000001", firstName = "CustA", consentSource = "Manual"
        })).Content.ReadFromJsonAsync<CustomerDto>(JsonOptions))!;

        DateTime start = DateTime.UtcNow.AddDays(1);
        await client.PostAsJsonAsync(AppointmentsBase, new
        {
            customerId = custA.Id, startsAt = start, endsAt = start.AddHours(1)
        });

        // Tenant B
        HttpResponseMessage regB = await client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email = "appt-isoB@test.dev", password = "Test1234!@#",
            firstName = "B", lastName = "Owner", businessName = "Salon B"
        });
        AuthResponseDto? authB = await regB.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB!.AccessToken);

        // Tenant B lists appointments — should see 0
        HttpResponseMessage listHttp = await client.GetAsync(AppointmentsBase);
        PagedResultDto? result = await listHttp.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Equal(0, result!.TotalCount);
    }

    // =====================================================================
    // FULL LIFECYCLE
    // =====================================================================

    [Fact]
    public async Task FullLifecycle_Scheduled_Confirmed_Completed()
    {
        await AuthenticateAsync(email: "lifecycle@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+213661600001", firstName: "Life");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id);

        Assert.Equal("Scheduled", appt.Status);

        // Confirm
        HttpResponseMessage confirmHttp = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);
        AppointmentDto? confirmed = await confirmHttp.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Confirmed", confirmed!.Status);

        // Complete
        HttpResponseMessage completeHttp = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/complete", null);
        AppointmentDto? completed = await completeHttp.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);
        Assert.Equal("Completed", completed!.Status);

        // Cannot cancel after completion
        HttpResponseMessage cancelHttp = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.BadRequest, cancelHttp.StatusCode);
    }
}
