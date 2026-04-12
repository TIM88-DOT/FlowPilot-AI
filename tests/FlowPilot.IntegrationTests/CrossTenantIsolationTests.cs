using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowPilot.IntegrationTests.Fixtures;

namespace FlowPilot.IntegrationTests;

/// <summary>
/// Comprehensive cross-tenant isolation tests.
/// Every test creates two tenants and verifies that tenant B cannot read,
/// modify, or search tenant A's data across all bounded contexts.
/// </summary>
public class CrossTenantIsolationTests : IClassFixture<FlowPilotApiFactory>
{
    private readonly FlowPilotApiFactory _factory;
    private const string AuthBase = "/api/v1/auth";
    private const string CustomersBase = "/api/v1/customers";
    private const string AppointmentsBase = "/api/v1/appointments";
    private const string MessagingBase = "/api/v1/messaging";
    private const string TemplatesBase = "/api/v1/templates";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CrossTenantIsolationTests(FlowPilotApiFactory factory)
    {
        _factory = factory;
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
    private record PagedAppointmentsDto(List<AppointmentDto> Items, int TotalCount, int Page, int PageSize, int TotalPages);
    private record PagedCustomersDto(List<CustomerDto> Items, int TotalCount, int Page, int PageSize, int TotalPages);
    private record TemplateDto(Guid Id, string Name, string? Description, string Category, bool IsSystem,
        List<LocaleVariantDto> LocaleVariants, DateTime CreatedAt, DateTime UpdatedAt);
    private record LocaleVariantDto(Guid Id, string Locale, string Body, int SegmentCount);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private HttpClient CreateClient() => _factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

    private static async Task<AuthResponseDto> RegisterAsync(HttpClient client, string email, string businessName)
    {
        HttpResponseMessage http = await client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email, password = "Test1234!@#",
            firstName = "Owner", lastName = "Test", businessName,
            businessPhone = "+14165550000", timezone = "America/Toronto", defaultLanguage = "fr"
        });
        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        AuthResponseDto? auth = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return auth;
    }

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string phone, string firstName)
    {
        HttpResponseMessage http = await client.PostAsJsonAsync(CustomersBase, new
        {
            phone, firstName, lastName = "Test", consentSource = "Manual"
        });
        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions))!;
    }

    private static async Task<AppointmentDto> CreateAppointmentAsync(HttpClient client, Guid customerId)
    {
        DateTime start = DateTime.UtcNow.AddDays(1);
        HttpResponseMessage http = await client.PostAsJsonAsync(AppointmentsBase, new
        {
            customerId, startsAt = start, endsAt = start.AddHours(1), serviceName = "Haircut"
        });
        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions))!;
    }

    private static void SwitchToToken(HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    // =====================================================================
    // CUSTOMER ISOLATION
    // =====================================================================

    [Fact]
    public async Task TenantB_CannotGetTenantA_Customer()
    {
        HttpClient client = CreateClient();
        AuthResponseDto authA = await RegisterAsync(client, "iso-cust-get-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167700001", "TenantACust");

        AuthResponseDto authB = await RegisterAsync(client, "iso-cust-get-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.GetAsync($"{CustomersBase}/{custA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    [Fact]
    public async Task TenantB_CannotListTenantA_Customers()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-cust-list-a@test.dev", "Salon A");
        await CreateCustomerAsync(client, "+14167700002", "InvisibleCust");

        AuthResponseDto authB = await RegisterAsync(client, "iso-cust-list-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.GetAsync(CustomersBase);
        PagedCustomersDto? result = await http.Content.ReadFromJsonAsync<PagedCustomersDto>(JsonOptions);
        Assert.Equal(0, result!.TotalCount);
    }

    [Fact]
    public async Task TenantB_CannotSearchTenantA_Customers()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-cust-search-a@test.dev", "Salon A");
        await CreateCustomerAsync(client, "+14167700003", "Unique_SearchName");

        AuthResponseDto authB = await RegisterAsync(client, "iso-cust-search-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.GetAsync($"{CustomersBase}?search=Unique_SearchName");
        PagedCustomersDto? result = await http.Content.ReadFromJsonAsync<PagedCustomersDto>(JsonOptions);
        Assert.Equal(0, result!.TotalCount);
    }

    [Fact]
    public async Task TenantB_CannotModifyTenantA_CustomerConsent()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-cust-consent-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167700004", "ConsentCust");

        AuthResponseDto authB = await RegisterAsync(client, "iso-cust-consent-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.PutAsJsonAsync($"{CustomersBase}/{custA.Id}/consent", new
        {
            status = "OptedIn", source = "Manual"
        });
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    // =====================================================================
    // APPOINTMENT ISOLATION
    // =====================================================================

    [Fact]
    public async Task TenantB_CannotGetTenantA_Appointment()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-appt-get-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167701001", "ApptCust");
        AppointmentDto apptA = await CreateAppointmentAsync(client, custA.Id);

        AuthResponseDto authB = await RegisterAsync(client, "iso-appt-get-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.GetAsync($"{AppointmentsBase}/{apptA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    [Fact]
    public async Task TenantB_CannotConfirmTenantA_Appointment()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-appt-confirm-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167701002", "ConfirmCust");
        AppointmentDto apptA = await CreateAppointmentAsync(client, custA.Id);

        AuthResponseDto authB = await RegisterAsync(client, "iso-appt-confirm-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.PostAsync($"{AppointmentsBase}/{apptA.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    [Fact]
    public async Task TenantB_CannotCancelTenantA_Appointment()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-appt-cancel-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167701003", "CancelCust");
        AppointmentDto apptA = await CreateAppointmentAsync(client, custA.Id);

        AuthResponseDto authB = await RegisterAsync(client, "iso-appt-cancel-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.PostAsync($"{AppointmentsBase}/{apptA.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    [Fact]
    public async Task TenantB_CannotCompleteTenantA_Appointment()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-appt-complete-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167701004", "CompleteCust");
        AppointmentDto apptA = await CreateAppointmentAsync(client, custA.Id);

        // Confirm as Tenant A first
        await client.PostAsync($"{AppointmentsBase}/{apptA.Id}/confirm", null);

        AuthResponseDto authB = await RegisterAsync(client, "iso-appt-complete-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.PostAsync($"{AppointmentsBase}/{apptA.Id}/complete", null);
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    [Fact]
    public async Task TenantB_CannotRescheduleTenantA_Appointment()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-appt-resched-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167701005", "ReschedCust");
        AppointmentDto apptA = await CreateAppointmentAsync(client, custA.Id);

        AuthResponseDto authB = await RegisterAsync(client, "iso-appt-resched-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        DateTime newStart = DateTime.UtcNow.AddDays(10);
        HttpResponseMessage http = await client.PostAsJsonAsync(
            $"{AppointmentsBase}/{apptA.Id}/reschedule",
            new { startsAt = newStart, endsAt = newStart.AddHours(1) });
        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    [Fact]
    public async Task TenantB_CannotSearchTenantA_Appointments()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-appt-search-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167701006", "UniqueApptSearch");
        await CreateAppointmentAsync(client, custA.Id);

        AuthResponseDto authB = await RegisterAsync(client, "iso-appt-search-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.GetAsync($"{AppointmentsBase}?search=UniqueApptSearch");
        PagedAppointmentsDto? result = await http.Content.ReadFromJsonAsync<PagedAppointmentsDto>(JsonOptions);
        Assert.Equal(0, result!.TotalCount);
    }

    // =====================================================================
    // WEBHOOK ISOLATION
    // =====================================================================

    [Fact]
    public async Task Webhook_SameExternalId_DifferentTenants_CreatesSeparateAppointments()
    {
        HttpClient clientA = CreateClient();
        await RegisterAsync(clientA, "iso-wh-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(clientA, "+14167702001", "WHCustA");

        HttpClient clientB = CreateClient();
        await RegisterAsync(clientB, "iso-wh-b@test.dev", "Salon B");
        CustomerDto custB = await CreateCustomerAsync(clientB, "+14167702002", "WHCustB");

        DateTime start = DateTime.UtcNow.AddDays(3);
        string sharedExternalId = "ext-shared-001";

        // Tenant A ingests with ExternalId "ext-shared-001"
        HttpResponseMessage httpA = await clientA.PostAsJsonAsync("/api/webhooks/appointments/inbound", new
        {
            externalId = sharedExternalId,
            customerId = custA.Id,
            startsAt = start, endsAt = start.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.OK, httpA.StatusCode);
        AppointmentDto? apptA = await httpA.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);

        // Tenant B ingests with same ExternalId — should create a SEPARATE appointment
        HttpResponseMessage httpB = await clientB.PostAsJsonAsync("/api/webhooks/appointments/inbound", new
        {
            externalId = sharedExternalId,
            customerId = custB.Id,
            startsAt = start, endsAt = start.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.OK, httpB.StatusCode);
        AppointmentDto? apptB = await httpB.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions);

        // Different appointments
        Assert.NotEqual(apptA!.Id, apptB!.Id);

        // Each tenant only sees their own
        HttpResponseMessage listA = await clientA.GetAsync(AppointmentsBase);
        PagedAppointmentsDto? resultA = await listA.Content.ReadFromJsonAsync<PagedAppointmentsDto>(JsonOptions);
        Assert.Equal(1, resultA!.TotalCount);
        Assert.Equal(apptA.Id, resultA.Items[0].Id);

        HttpResponseMessage listB = await clientB.GetAsync(AppointmentsBase);
        PagedAppointmentsDto? resultB = await listB.Content.ReadFromJsonAsync<PagedAppointmentsDto>(JsonOptions);
        Assert.Equal(1, resultB!.TotalCount);
        Assert.Equal(apptB.Id, resultB.Items[0].Id);
    }

    // =====================================================================
    // MESSAGING ISOLATION
    // =====================================================================

    [Fact]
    public async Task TenantB_CannotSendSmsTenantA_Customer()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-msg-a@test.dev", "Salon A");
        CustomerDto custA = await CreateCustomerAsync(client, "+14167703001", "MsgCust");

        // Opt in as Tenant A
        await client.PutAsJsonAsync($"{CustomersBase}/{custA.Id}/consent", new
        {
            status = "OptedIn", source = "Manual"
        });

        AuthResponseDto authB = await RegisterAsync(client, "iso-msg-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        // Tenant B tries to send raw SMS to Tenant A's customer
        HttpResponseMessage http = await client.PostAsJsonAsync($"{MessagingBase}/send-raw", new
        {
            customerId = custA.Id,
            body = "Hello from wrong tenant"
        });

        // Should fail — customer not found in Tenant B's scope
        Assert.True(
            http.StatusCode == HttpStatusCode.NotFound || http.StatusCode == HttpStatusCode.BadRequest,
            $"Expected 404 or 400, got {http.StatusCode}");
    }

    // =====================================================================
    // TEMPLATE ISOLATION
    // =====================================================================

    [Fact]
    public async Task TenantB_CannotSeeTenantA_CustomTemplates()
    {
        HttpClient client = CreateClient();
        await RegisterAsync(client, "iso-tpl-a@test.dev", "Salon A");

        // Tenant A creates a custom template
        await client.PostAsJsonAsync(TemplatesBase, new
        {
            name = "secret_promo",
            description = "Tenant A exclusive",
            category = "Marketing",
            localeVariants = new[] { new { locale = "fr", body = "Promo secret de Salon A!" } }
        });

        AuthResponseDto authB = await RegisterAsync(client, "iso-tpl-b@test.dev", "Salon B");
        SwitchToToken(client, authB.AccessToken);

        HttpResponseMessage http = await client.GetAsync(TemplatesBase);
        List<TemplateDto>? templates = await http.Content.ReadFromJsonAsync<List<TemplateDto>>(JsonOptions);

        // Tenant B should see their own system templates, but NOT "secret_promo"
        Assert.DoesNotContain(templates!, t => t.Name == "secret_promo");
    }
}
