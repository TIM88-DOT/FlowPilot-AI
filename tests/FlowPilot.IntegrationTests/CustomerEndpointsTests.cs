using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FlowPilot.IntegrationTests.Fixtures;

namespace FlowPilot.IntegrationTests;

/// <summary>
/// Integration tests for /api/v1/customers endpoints:
/// CRUD, consent management, GDPR anonymize, CSV import, and cross-tenant isolation.
/// Each test class gets its own isolated PostgreSQL database.
/// </summary>
public class CustomerEndpointsTests : IClassFixture<FlowPilotApiFactory>
{
    private readonly HttpClient _client;
    private const string AuthBase = "/api/v1/auth";
    private const string CustomersBase = "/api/v1/customers";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CustomerEndpointsTests(FlowPilotApiFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
    }

    // -----------------------------------------------------------------------
    // DTOs for deserialization
    // -----------------------------------------------------------------------

    private record AuthResponseDto(
        string AccessToken,
        DateTime ExpiresAt,
        UserInfoDto User);

    private record UserInfoDto(
        Guid Id,
        Guid TenantId,
        string Email,
        string FirstName,
        string LastName,
        string Role,
        string BusinessName);

    private record CustomerDto(
        Guid Id,
        string Phone,
        string? Email,
        string FirstName,
        string? LastName,
        string PreferredLanguage,
        string? Tags,
        decimal NoShowScore,
        string ConsentStatus,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private record ConsentRecordDto(
        Guid Id,
        string Status,
        string Source,
        string? Notes,
        DateTime CreatedAt);

    private record PagedResultDto(
        List<CustomerDto> Items,
        int TotalCount,
        int Page,
        int PageSize,
        int TotalPages);

    private record CsvImportResultDto(
        int TotalRows,
        int Imported,
        int Skipped,
        List<CsvRowErrorDto> Errors);

    private record CsvRowErrorDto(
        int Row,
        string Error);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Registers a tenant and sets the Bearer token on the shared client.
    /// Returns the auth response so tests can inspect tenant/user info.
    /// </summary>
    private async Task<AuthResponseDto> AuthenticateAsync(
        string email = "owner@test.dev",
        string businessName = "Salon Prestige")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email,
            password = "Test1234!@#",
            firstName = "Alex",
            lastName = "Tremblay",
            businessName,
            businessPhone = "+14165551234",
            timezone = "America/Toronto",
            defaultLanguage = "fr"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        AuthResponseDto? auth = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return auth;
    }

    private static object MakeCustomerPayload(
        string phone = "+14165550001",
        string firstName = "Karim",
        string? lastName = "Hadj",
        string? email = "karim@test.dev",
        string preferredLanguage = "fr",
        string? tags = null,
        string consentSource = "Manual",
        string? consentNotes = null) => new
    {
        phone,
        firstName,
        lastName,
        email,
        preferredLanguage,
        tags,
        consentSource,
        consentNotes
    };

    /// <summary>
    /// Creates a customer and returns the deserialized DTO.
    /// </summary>
    private async Task<CustomerDto> CreateCustomerAsync(
        string phone = "+14165550001",
        string firstName = "Karim",
        string? tags = null)
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(
            CustomersBase,
            MakeCustomerPayload(phone: phone, firstName: firstName, tags: tags));

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        CustomerDto? dto = await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        return dto!;
    }

    // =====================================================================
    // AUTH REQUIRED
    // =====================================================================

    [Fact]
    public async Task AllEndpoints_WithoutAuth_Return401()
    {
        // Use a fresh client with no auth header
        using var factory = new FlowPilotApiFactory();
        await factory.InitializeAsync();
        HttpClient anonClient = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.GetAsync(CustomersBase)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.PostAsJsonAsync(CustomersBase, MakeCustomerPayload())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.GetAsync($"{CustomersBase}/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.PutAsJsonAsync($"{CustomersBase}/{Guid.NewGuid()}", new { firstName = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonClient.DeleteAsync($"{CustomersBase}/{Guid.NewGuid()}")).StatusCode);
    }

    // =====================================================================
    // CREATE
    // =====================================================================

    [Fact]
    public async Task Create_ValidRequest_Returns201WithCustomer()
    {
        await AuthenticateAsync(email: "create-ok@test.dev");

        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, MakeCustomerPayload(
            phone: "+14165551001",
            firstName: "Liam",
            lastName: "Dubois",
            email: "liam@test.dev",
            tags: "vip,regular"));

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);

        CustomerDto? customer = await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        Assert.NotNull(customer);
        Assert.Equal("+14165551001", customer!.Phone);
        Assert.Equal("Liam", customer.FirstName);
        Assert.Equal("Dubois", customer.LastName);
        Assert.Equal("liam@test.dev", customer.Email);
        Assert.Equal("fr", customer.PreferredLanguage);
        Assert.Equal("vip,regular", customer.Tags);
        Assert.Equal("Pending", customer.ConsentStatus);
        Assert.NotEqual(Guid.Empty, customer.Id);
    }

    [Fact]
    public async Task Create_NormalizesLocalPhoneToE164()
    {
        await AuthenticateAsync(email: "normalize@test.dev");

        // Local Canadian number without country code
        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, MakeCustomerPayload(
            phone: "4165552001",
            firstName: "Chloe"));

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);

        CustomerDto? customer = await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        Assert.Equal("+14165552001", customer!.Phone);
    }

    [Fact]
    public async Task Create_InvalidPhone_Returns400()
    {
        await AuthenticateAsync(email: "bad-phone@test.dev");

        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, MakeCustomerPayload(
            phone: "not-a-phone",
            firstName: "Test"));

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicatePhone_Returns409()
    {
        await AuthenticateAsync(email: "dup-phone@test.dev");

        await CreateCustomerAsync(phone: "+14165553001", firstName: "First");

        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, MakeCustomerPayload(
            phone: "+14165553001",
            firstName: "Second"));

        Assert.Equal(HttpStatusCode.Conflict, http.StatusCode);
    }

    [Fact]
    public async Task Create_InitialConsentRecordIsCreated()
    {
        await AuthenticateAsync(email: "consent-init@test.dev");

        CustomerDto customer = await CreateCustomerAsync(phone: "+14165554001", firstName: "Sara");

        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}/{customer.Id}/history");
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);

        List<ConsentRecordDto>? history = await http.Content.ReadFromJsonAsync<List<ConsentRecordDto>>(JsonOptions);
        Assert.NotNull(history);
        Assert.Single(history!);
        Assert.Equal("Pending", history[0].Status);
        Assert.Equal("Manual", history[0].Source);
    }

    // =====================================================================
    // GET BY ID
    // =====================================================================

    [Fact]
    public async Task GetById_ExistingCustomer_Returns200()
    {
        await AuthenticateAsync(email: "get-ok@test.dev");
        CustomerDto created = await CreateCustomerAsync(phone: "+14165555001", firstName: "Rami");

        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        CustomerDto? customer = await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        Assert.Equal(created.Id, customer!.Id);
        Assert.Equal("Rami", customer.FirstName);
    }

    [Fact]
    public async Task GetById_NonExistent_Returns404()
    {
        await AuthenticateAsync(email: "get-404@test.dev");

        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    // =====================================================================
    // LIST
    // =====================================================================

    [Fact]
    public async Task List_ReturnsPaginatedResults()
    {
        await AuthenticateAsync(email: "list-ok@test.dev");

        await CreateCustomerAsync(phone: "+14165556001", firstName: "Ali");
        await CreateCustomerAsync(phone: "+14165556002", firstName: "Omar");
        await CreateCustomerAsync(phone: "+14165556003", firstName: "Nour");

        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}?page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        PagedResultDto? result = await http.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(3, result!.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.TotalPages);
    }

    [Fact]
    public async Task List_SearchByName_FiltersResults()
    {
        await AuthenticateAsync(email: "search-name@test.dev");

        await CreateCustomerAsync(phone: "+14165557001", firstName: "Mourad");
        await CreateCustomerAsync(phone: "+14165557002", firstName: "Samira");

        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}?search=mourad");

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        PagedResultDto? result = await http.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Single(result!.Items);
        Assert.Equal("Mourad", result.Items[0].FirstName);
    }

    [Fact]
    public async Task List_FilterByTag_FiltersResults()
    {
        await AuthenticateAsync(email: "filter-tag@test.dev");

        await CreateCustomerAsync(phone: "+14165558001", firstName: "Amine", tags: "vip,loyal");
        await CreateCustomerAsync(phone: "+14165558002", firstName: "Nadia", tags: "new");

        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}?tag=vip");

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        PagedResultDto? result = await http.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Single(result!.Items);
        Assert.Equal("Amine", result.Items[0].FirstName);
    }

    [Fact]
    public async Task List_FilterByConsentStatus_FiltersResults()
    {
        await AuthenticateAsync(email: "filter-consent@test.dev");

        CustomerDto customer = await CreateCustomerAsync(phone: "+14165559001", firstName: "Hichem");

        // Opt in one customer
        await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}/consent", new
        {
            status = "OptedIn",
            source = "Manual"
        });

        await CreateCustomerAsync(phone: "+14165559002", firstName: "Leila"); // stays Pending

        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}?consentStatus=OptedIn");

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        PagedResultDto? result = await http.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Single(result!.Items);
        Assert.Equal("Hichem", result.Items[0].FirstName);
    }

    // =====================================================================
    // UPDATE
    // =====================================================================

    [Fact]
    public async Task Update_PartialFields_Returns200()
    {
        await AuthenticateAsync(email: "update-ok@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+14166000001", firstName: "Rachid");

        HttpResponseMessage http = await _client.PutAsJsonAsync($"{CustomersBase}/{customer.Id}", new
        {
            firstName = "Rachid-Updated",
            tags = "premium"
        });

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        CustomerDto? updated = await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        Assert.Equal("Rachid-Updated", updated!.FirstName);
        Assert.Equal("premium", updated.Tags);
        Assert.Equal(customer.Phone, updated.Phone); // unchanged
    }

    [Fact]
    public async Task Update_PhoneChange_NormalizesAndChecksUniqueness()
    {
        await AuthenticateAsync(email: "update-phone@test.dev");
        CustomerDto c1 = await CreateCustomerAsync(phone: "+14166100001", firstName: "A");
        CustomerDto c2 = await CreateCustomerAsync(phone: "+14166100002", firstName: "B");

        // Try to change c2's phone to c1's phone — should fail
        HttpResponseMessage http = await _client.PutAsJsonAsync($"{CustomersBase}/{c2.Id}", new
        {
            phone = "+14166100001"
        });

        Assert.Equal(HttpStatusCode.Conflict, http.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistent_Returns404()
    {
        await AuthenticateAsync(email: "update-404@test.dev");

        HttpResponseMessage http = await _client.PutAsJsonAsync($"{CustomersBase}/{Guid.NewGuid()}", new
        {
            firstName = "Ghost"
        });

        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    // =====================================================================
    // DELETE (GDPR ANONYMIZE)
    // =====================================================================

    [Fact]
    public async Task Delete_AnonymizesAndSoftDeletes()
    {
        await AuthenticateAsync(email: "delete-ok@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+14166200001", firstName: "ToDelete");

        HttpResponseMessage deleteHttp = await _client.DeleteAsync($"{CustomersBase}/{customer.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteHttp.StatusCode);

        // Customer should no longer appear in GET (soft-deleted, filtered out)
        HttpResponseMessage getHttp = await _client.GetAsync($"{CustomersBase}/{customer.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getHttp.StatusCode);
    }

    [Fact]
    public async Task Delete_SoftDeletedCustomer_NotInList()
    {
        await AuthenticateAsync(email: "delete-list@test.dev");

        await CreateCustomerAsync(phone: "+14166300001", firstName: "Visible");
        CustomerDto toDelete = await CreateCustomerAsync(phone: "+14166300002", firstName: "Hidden");

        await _client.DeleteAsync($"{CustomersBase}/{toDelete.Id}");

        HttpResponseMessage http = await _client.GetAsync(CustomersBase);
        PagedResultDto? result = await http.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Single(result!.Items);
        Assert.Equal("Visible", result.Items[0].FirstName);
    }

    [Fact]
    public async Task Delete_NonExistent_Returns404()
    {
        await AuthenticateAsync(email: "delete-404@test.dev");

        HttpResponseMessage http = await _client.DeleteAsync($"{CustomersBase}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    // =====================================================================
    // CONSENT MANAGEMENT
    // =====================================================================

    [Fact]
    public async Task UpdateConsent_ChangesStatusAndAppendsRecord()
    {
        await AuthenticateAsync(email: "consent-ok@test.dev");
        CustomerDto customer = await CreateCustomerAsync(phone: "+14166400001", firstName: "Consent");

        // Opt in
        HttpResponseMessage optInHttp = await _client.PutAsJsonAsync(
            $"{CustomersBase}/{customer.Id}/consent",
            new { status = "OptedIn", source = "Manual", notes = "Customer agreed verbally" });

        Assert.Equal(HttpStatusCode.OK, optInHttp.StatusCode);
        CustomerDto? optedIn = await optInHttp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        Assert.Equal("OptedIn", optedIn!.ConsentStatus);

        // Opt out
        HttpResponseMessage optOutHttp = await _client.PutAsJsonAsync(
            $"{CustomersBase}/{customer.Id}/consent",
            new { status = "OptedOut", source = "SmsOptOut", notes = "Sent STOP" });

        Assert.Equal(HttpStatusCode.OK, optOutHttp.StatusCode);
        CustomerDto? optedOut = await optOutHttp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);
        Assert.Equal("OptedOut", optedOut!.ConsentStatus);

        // History should have 3 records: initial Pending + OptedIn + OptedOut
        HttpResponseMessage historyHttp = await _client.GetAsync($"{CustomersBase}/{customer.Id}/history");
        List<ConsentRecordDto>? history = await historyHttp.Content.ReadFromJsonAsync<List<ConsentRecordDto>>(JsonOptions);
        Assert.Equal(3, history!.Count);
        // Ordered by CreatedAt descending
        Assert.Equal("OptedOut", history[0].Status);
        Assert.Equal("OptedIn", history[1].Status);
        Assert.Equal("Pending", history[2].Status);
    }

    [Fact]
    public async Task GetConsentHistory_NonExistentCustomer_Returns404()
    {
        await AuthenticateAsync(email: "history-404@test.dev");

        HttpResponseMessage http = await _client.GetAsync($"{CustomersBase}/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.NotFound, http.StatusCode);
    }

    // =====================================================================
    // CSV IMPORT
    // =====================================================================

    [Fact]
    public async Task ImportCsv_ValidFile_ImportsAndNormalizesPhones()
    {
        await AuthenticateAsync(email: "csv-ok@test.dev");

        string csv = "phone,firstname,lastname,email,language,tags\n" +
                      "+14165511001,Ali,Kaci,ali@test.dev,fr,vip\n" +
                      "4165511002,Nadia,Boua,,en,\n";

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(csv))), "file", "customers.csv");

        HttpResponseMessage http = await _client.PostAsync($"{CustomersBase}/import", content);

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        CsvImportResultDto? result = await http.Content.ReadFromJsonAsync<CsvImportResultDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(2, result!.TotalRows);
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Errors);

        // Verify customers were created with normalized phones
        HttpResponseMessage listHttp = await _client.GetAsync(CustomersBase);
        PagedResultDto? customers = await listHttp.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);
        Assert.Equal(2, customers!.TotalCount);

        // The local number should be normalized
        Assert.Contains(customers.Items, c => c.Phone == "+14165511002");
    }

    [Fact]
    public async Task ImportCsv_DuplicatesInDb_AreSkipped()
    {
        await AuthenticateAsync(email: "csv-skip@test.dev");

        // Pre-create a customer
        await CreateCustomerAsync(phone: "+14165512001", firstName: "Existing");

        string csv = "phone,firstname\n" +
                      "+14165512001,Duplicate\n" +
                      "+14165512002,New\n";

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(csv))), "file", "customers.csv");

        HttpResponseMessage http = await _client.PostAsync($"{CustomersBase}/import", content);

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        CsvImportResultDto? result = await http.Content.ReadFromJsonAsync<CsvImportResultDto>(JsonOptions);
        Assert.Equal(1, result!.Imported);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task ImportCsv_InvalidPhones_ReportedAsErrors()
    {
        await AuthenticateAsync(email: "csv-err@test.dev");

        string csv = "phone,firstname\n" +
                      "not-a-number,BadPhone\n" +
                      "+14165513001,GoodPhone\n";

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(csv))), "file", "customers.csv");

        HttpResponseMessage http = await _client.PostAsync($"{CustomersBase}/import", content);

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        CsvImportResultDto? result = await http.Content.ReadFromJsonAsync<CsvImportResultDto>(JsonOptions);
        Assert.Equal(1, result!.Imported);
        Assert.Single(result.Errors);
        Assert.Equal(2, result.Errors[0].Row);
    }

    [Fact]
    public async Task ImportCsv_MissingRequiredColumns_Returns400()
    {
        await AuthenticateAsync(email: "csv-bad@test.dev");

        string csv = "email,lastname\n" +
                      "test@test.dev,Test\n";

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(csv))), "file", "customers.csv");

        HttpResponseMessage http = await _client.PostAsync($"{CustomersBase}/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, http.StatusCode);
    }

    [Fact]
    public async Task ImportCsv_AllImportedCustomersHavePendingConsent()
    {
        await AuthenticateAsync(email: "csv-consent@test.dev");

        string csv = "phone,firstname\n" +
                      "+14165514001,Ahmed\n" +
                      "+14165514002,Fatima\n";

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(csv))), "file", "customers.csv");

        await _client.PostAsync($"{CustomersBase}/import", content);

        HttpResponseMessage listHttp = await _client.GetAsync(CustomersBase);
        PagedResultDto? customers = await listHttp.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);

        Assert.All(customers!.Items, c => Assert.Equal("Pending", c.ConsentStatus));
    }

    // =====================================================================
    // CROSS-TENANT ISOLATION
    // =====================================================================

    [Fact]
    public async Task CrossTenant_CannotSeeOtherTenantsCustomers()
    {
        // Tenant A creates a customer
        using var factoryA = new FlowPilotApiFactory();
        await factoryA.InitializeAsync();
        HttpClient clientA = factoryA.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

        HttpResponseMessage regA = await clientA.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email = "ownerA@test.dev", password = "Test1234!@#",
            firstName = "A", lastName = "Owner", businessName = "Salon A"
        });
        AuthResponseDto? authA = await regA.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA!.AccessToken);

        HttpResponseMessage createHttp = await clientA.PostAsJsonAsync(CustomersBase, MakeCustomerPayload(
            phone: "+14167000001", firstName: "TenantA-Customer"));
        Assert.Equal(HttpStatusCode.Created, createHttp.StatusCode);

        // Tenant B registers and lists customers — should see zero
        HttpResponseMessage regB = await clientA.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email = "ownerB@test.dev", password = "Test1234!@#",
            firstName = "B", lastName = "Owner", businessName = "Salon B"
        });
        AuthResponseDto? authB = await regB.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB!.AccessToken);

        HttpResponseMessage listHttp = await clientA.GetAsync(CustomersBase);
        PagedResultDto? result = await listHttp.Content.ReadFromJsonAsync<PagedResultDto>(JsonOptions);

        Assert.Equal(0, result!.TotalCount);
    }

    [Fact]
    public async Task CrossTenant_CannotAccessOtherTenantsCustomerById()
    {
        // Tenant A creates a customer
        using var factory = new FlowPilotApiFactory();
        await factory.InitializeAsync();
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });

        HttpResponseMessage regA = await client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email = "isoA@test.dev", password = "Test1234!@#",
            firstName = "A", lastName = "Owner", businessName = "Salon A"
        });
        AuthResponseDto? authA = await regA.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authA!.AccessToken);

        HttpResponseMessage createHttp = await client.PostAsJsonAsync(CustomersBase, MakeCustomerPayload(
            phone: "+14167100001", firstName: "Secret"));
        CustomerDto? customerA = await createHttp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);

        // Switch to Tenant B
        HttpResponseMessage regB = await client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email = "isoB@test.dev", password = "Test1234!@#",
            firstName = "B", lastName = "Owner", businessName = "Salon B"
        });
        AuthResponseDto? authB = await regB.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authB!.AccessToken);

        // Try to access Tenant A's customer — should get 404 (filtered by tenant)
        HttpResponseMessage getHttp = await client.GetAsync($"{CustomersBase}/{customerA!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getHttp.StatusCode);
    }
}
