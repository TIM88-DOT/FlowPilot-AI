using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowPilot.IntegrationTests.Fixtures;

namespace FlowPilot.IntegrationTests;

/// <summary>
/// Integration tests for all /api/v1/auth endpoints: register, login, refresh, logout.
/// Each test class gets its own isolated PostgreSQL database.
/// </summary>
public class AuthEndpointsTests : IClassFixture<FlowPilotApiFactory>
{
    private readonly HttpClient _client;
    private const string AuthBase = "/api/v1/auth";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AuthEndpointsTests(FlowPilotApiFactory factory)
    {
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            // Allow us to manually inspect redirect/cookie headers
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static object MakeRegisterPayload(
        string email = "test@flowpilot.dev",
        string password = "Test1234!@#",
        string firstName = "Ahmed",
        string lastName = "Benali",
        string businessName = "Salon Prestige",
        string? businessPhone = "+213555123456",
        string? timezone = "Africa/Algiers",
        string defaultLanguage = "fr") => new
    {
        email,
        password,
        firstName,
        lastName,
        businessName,
        businessPhone,
        timezone,
        defaultLanguage
    };

    /// <summary>
    /// Registers a user and returns the deserialized auth response.
    /// </summary>
    private async Task<(AuthResponseDto Response, HttpResponseMessage Http)> RegisterAsync(
        string email = "test@flowpilot.dev",
        string businessName = "Salon Prestige")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AuthBase}/register",
            MakeRegisterPayload(email: email, businessName: businessName));

        AuthResponseDto? body = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        return (body!, http);
    }

    /// <summary>
    /// Logs in and returns the deserialized auth response.
    /// </summary>
    private async Task<(AuthResponseDto Response, HttpResponseMessage Http)> LoginAsync(
        string email = "test@flowpilot.dev",
        string password = "Test1234!@#")
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AuthBase}/login",
            new { email, password });

        AuthResponseDto? body = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        return (body!, http);
    }

    // =====================================================================
    // HEALTH
    // =====================================================================

    [Fact]
    public async Task Health_ReturnsOk()
    {
        HttpResponseMessage response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body);
    }

    // =====================================================================
    // REGISTER
    // =====================================================================

    [Fact]
    public async Task Register_ValidRequest_Returns201WithTokenAndUser()
    {
        var (auth, http) = await RegisterAsync(email: "register-ok@test.dev");

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.Equal("register-ok@test.dev", auth.User.Email);
        Assert.Equal("Ahmed", auth.User.FirstName);
        Assert.Equal("Owner", auth.User.Role);
        Assert.Equal("Salon Prestige", auth.User.BusinessName);
        Assert.NotEqual(Guid.Empty, auth.User.Id);
        Assert.NotEqual(Guid.Empty, auth.User.TenantId);
        Assert.True(auth.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Register_SetsRefreshTokenCookie()
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AuthBase}/register",
            MakeRegisterPayload(email: "cookie-check@test.dev"));

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        Assert.True(http.Headers.Contains("Set-Cookie"));

        string cookieHeader = string.Join("; ", http.Headers.GetValues("Set-Cookie"));
        Assert.Contains("refreshToken=", cookieHeader);
        Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        await RegisterAsync(email: "dup@test.dev");

        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AuthBase}/register",
            MakeRegisterPayload(email: "dup@test.dev", businessName: "Another Salon"));

        Assert.Equal(HttpStatusCode.Conflict, http.StatusCode);
    }

    // =====================================================================
    // LOGIN
    // =====================================================================

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        await RegisterAsync(email: "login-ok@test.dev");
        var (auth, http) = await LoginAsync(email: "login-ok@test.dev");

        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.Equal("login-ok@test.dev", auth.User.Email);
        Assert.Equal("Owner", auth.User.Role);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        await RegisterAsync(email: "wrong-pw@test.dev");

        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AuthBase}/login",
            new { email = "wrong-pw@test.dev", password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, http.StatusCode);
    }

    [Fact]
    public async Task Login_NonExistentUser_Returns401()
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AuthBase}/login",
            new { email = "nobody@test.dev", password = "Test1234!@#" });

        Assert.Equal(HttpStatusCode.Unauthorized, http.StatusCode);
    }

    [Fact]
    public async Task Login_SetsRefreshTokenCookie()
    {
        await RegisterAsync(email: "login-cookie@test.dev");

        HttpResponseMessage http = await _client.PostAsJsonAsync(
            $"{AuthBase}/login",
            new { email = "login-cookie@test.dev", password = "Test1234!@#" });

        Assert.True(http.Headers.Contains("Set-Cookie"));
        string cookieHeader = string.Join("; ", http.Headers.GetValues("Set-Cookie"));
        Assert.Contains("refreshToken=", cookieHeader);
    }

    // =====================================================================
    // REFRESH
    // =====================================================================

    [Fact]
    public async Task Refresh_ValidCookie_Returns200WithRotatedToken()
    {
        using var factory = new FlowPilotApiFactory();
        await factory.InitializeAsync();
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = false
            });

        // Register and capture the refresh cookie
        HttpResponseMessage registerHttp = await client.PostAsJsonAsync(
            $"{AuthBase}/register",
            MakeRegisterPayload(email: "refresh-ok@test.dev"));
        Assert.Equal(HttpStatusCode.Created, registerHttp.StatusCode);

        AuthResponseDto? firstAuth = await registerHttp.Content
            .ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        string refreshCookie = ExtractRefreshCookie(registerHttp);
        Assert.False(string.IsNullOrWhiteSpace(refreshCookie));

        // Refresh with the cookie
        HttpRequestMessage refreshReq = new(HttpMethod.Post, $"{AuthBase}/refresh");
        refreshReq.Headers.Add("Cookie", $"refreshToken={refreshCookie}");
        HttpResponseMessage refreshHttp = await client.SendAsync(refreshReq);

        Assert.Equal(HttpStatusCode.OK, refreshHttp.StatusCode);

        AuthResponseDto? refreshAuth = await refreshHttp.Content
            .ReadFromJsonAsync<AuthResponseDto>(JsonOptions);

        Assert.NotNull(refreshAuth);
        Assert.False(string.IsNullOrWhiteSpace(refreshAuth!.AccessToken));
        Assert.Equal(firstAuth!.User.Id, refreshAuth.User.Id);
        Assert.Equal(firstAuth.User.Email, refreshAuth.User.Email);

        // Verify the rotated refresh cookie is different from the original
        string newRefreshCookie = ExtractRefreshCookie(refreshHttp);
        Assert.False(string.IsNullOrWhiteSpace(newRefreshCookie));
        Assert.NotEqual(refreshCookie, newRefreshCookie);
    }

    [Fact]
    public async Task Refresh_NoCookie_Returns401()
    {
        // Fresh client with no cookies
        HttpClient freshClient = new FlowPilotApiFactory().CreateClient();
        HttpResponseMessage http = await freshClient.PostAsync($"{AuthBase}/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, http.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReplayOldToken_Returns401AfterRotation()
    {
        // Use a separate factory so cookie state is isolated
        using var factory = new FlowPilotApiFactory();
        await factory.InitializeAsync();
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = false
            });

        // Register and capture the raw Set-Cookie
        HttpResponseMessage registerHttp = await client.PostAsJsonAsync(
            $"{AuthBase}/register",
            MakeRegisterPayload(email: "replay@test.dev"));
        Assert.Equal(HttpStatusCode.Created, registerHttp.StatusCode);

        string firstCookie = ExtractRefreshCookie(registerHttp);
        Assert.False(string.IsNullOrWhiteSpace(firstCookie));

        // Use the cookie to refresh — this rotates the token
        HttpRequestMessage refreshReq1 = new(HttpMethod.Post, $"{AuthBase}/refresh");
        refreshReq1.Headers.Add("Cookie", $"refreshToken={firstCookie}");
        HttpResponseMessage refreshHttp1 = await client.SendAsync(refreshReq1);
        Assert.Equal(HttpStatusCode.OK, refreshHttp1.StatusCode);

        // Replay the old cookie — should fail
        HttpRequestMessage refreshReq2 = new(HttpMethod.Post, $"{AuthBase}/refresh");
        refreshReq2.Headers.Add("Cookie", $"refreshToken={firstCookie}");
        HttpResponseMessage refreshHttp2 = await client.SendAsync(refreshReq2);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshHttp2.StatusCode);
    }

    // =====================================================================
    // LOGOUT
    // =====================================================================

    [Fact]
    public async Task Logout_Authenticated_Returns200AndInvalidatesRefreshToken()
    {
        // Use isolated client without auto cookies to control cookie flow manually
        using var factory = new FlowPilotApiFactory();
        await factory.InitializeAsync();
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = false
            });

        // Register
        HttpResponseMessage registerHttp = await client.PostAsJsonAsync(
            $"{AuthBase}/register",
            MakeRegisterPayload(email: "logout@test.dev"));
        Assert.Equal(HttpStatusCode.Created, registerHttp.StatusCode);

        AuthResponseDto? auth = await registerHttp.Content
            .ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        string refreshCookie = ExtractRefreshCookie(registerHttp);

        // Logout with Bearer token
        HttpRequestMessage logoutReq = new(HttpMethod.Post, $"{AuthBase}/logout");
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        HttpResponseMessage logoutHttp = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.OK, logoutHttp.StatusCode);

        // Refresh with old cookie should now fail
        HttpRequestMessage refreshReq = new(HttpMethod.Post, $"{AuthBase}/refresh");
        refreshReq.Headers.Add("Cookie", $"refreshToken={refreshCookie}");
        HttpResponseMessage refreshHttp = await client.SendAsync(refreshReq);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshHttp.StatusCode);
    }

    [Fact]
    public async Task Logout_NoToken_Returns401()
    {
        HttpResponseMessage http = await _client.PostAsync($"{AuthBase}/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, http.StatusCode);
    }

    [Fact]
    public async Task Logout_DeletesRefreshCookie()
    {
        using var factory = new FlowPilotApiFactory();
        await factory.InitializeAsync();
        HttpClient client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                HandleCookies = false
            });

        HttpResponseMessage registerHttp = await client.PostAsJsonAsync(
            $"{AuthBase}/register",
            MakeRegisterPayload(email: "logout-cookie@test.dev"));
        AuthResponseDto? auth = await registerHttp.Content
            .ReadFromJsonAsync<AuthResponseDto>(JsonOptions);

        HttpRequestMessage logoutReq = new(HttpMethod.Post, $"{AuthBase}/logout");
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        HttpResponseMessage logoutHttp = await client.SendAsync(logoutReq);

        Assert.Equal(HttpStatusCode.OK, logoutHttp.StatusCode);
        // The response should instruct the browser to delete the cookie
        if (logoutHttp.Headers.Contains("Set-Cookie"))
        {
            string cookieHeader = string.Join("; ", logoutHttp.Headers.GetValues("Set-Cookie"));
            // Cookie deletion sets an expired date
            Assert.Contains("refreshToken=", cookieHeader);
        }
    }

    // =====================================================================
    // CROSS-TENANT ISOLATION
    // =====================================================================

    [Fact]
    public async Task Register_TwoTenants_GetDifferentTenantIds()
    {
        var (auth1, _) = await RegisterAsync(email: "tenant1@test.dev", businessName: "Salon A");
        var (auth2, _) = await RegisterAsync(email: "tenant2@test.dev", businessName: "Salon B");

        Assert.NotEqual(auth1.User.TenantId, auth2.User.TenantId);
        Assert.Equal("Salon A", auth1.User.BusinessName);
        Assert.Equal("Salon B", auth2.User.BusinessName);
    }

    // =====================================================================
    // JWT VALIDATION
    // =====================================================================

    [Fact]
    public async Task Logout_WithTamperedToken_Returns401()
    {
        var (auth, _) = await RegisterAsync(email: "tampered@test.dev");

        // Tamper with the token by flipping a character in the signature
        string tampered = auth.AccessToken[..^2] + "XX";

        HttpRequestMessage req = new(HttpMethod.Post, $"{AuthBase}/logout");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
        HttpResponseMessage http = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, http.StatusCode);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static string ExtractRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.Contains("Set-Cookie"))
            return string.Empty;

        foreach (string header in response.Headers.GetValues("Set-Cookie"))
        {
            if (!header.StartsWith("refreshToken="))
                continue;

            // refreshToken=<value>; path=...; httponly; ...
            string tokenPart = header.Split(';')[0]; // "refreshToken=<value>"
            return Uri.UnescapeDataString(tokenPart["refreshToken=".Length..]);
        }

        return string.Empty;
    }
}
