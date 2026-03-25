using System.Text;
using System.Text.Json.Serialization;
using FlowPilot.Api.Services;
using FlowPilot.Application.Auth;
using FlowPilot.Application.Customers;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Auth;
using FlowPilot.Infrastructure.Customers;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Seq(context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341"));

// ---------------------------------------------------------------------------
// JSON — accept enum values as strings (e.g. "Manual" instead of 0)
// ---------------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));

// ---------------------------------------------------------------------------
// Infrastructure
// ---------------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentTenant, HttpCurrentTenant>();
builder.Services.AddScoped<IFeatureGate, FeatureGateService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------------------------------------------------------------------------
// Auth services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();

// ---------------------------------------------------------------------------
// JWT Authentication
// ---------------------------------------------------------------------------
string jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Prevent .NET from remapping "sub" to long XML claim URIs
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ClockSkew = TimeSpan.Zero
    };
});

// ---------------------------------------------------------------------------
// Authorization — Role-based policies
// ---------------------------------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Owner", policy => policy.RequireClaim("role", "Owner"));
    options.AddPolicy("ManagerOrAbove", policy => policy.RequireClaim("role", "Owner", "Manager"));
    options.AddPolicy("Staff", policy => policy.RequireClaim("role", "Owner", "Manager", "Staff"));
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Middleware pipeline
// ---------------------------------------------------------------------------
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------
// Auth Endpoints — /api/v1/auth
// ---------------------------------------------------------------------------
RouteGroupBuilder authGroup = app.MapGroup("/api/v1/auth");

authGroup.MapPost("/register", async (RegisterRequest request, IAuthService authService, HttpContext httpContext, CancellationToken ct) =>
{
    Result<AuthResponse> result = await authService.RegisterAsync(request, ct);

    if (result.IsFailure)
        return Results.Problem(result.Error.Description, statusCode: result.Error.Code == "Auth.EmailTaken" ? 409 : 400);

    SetRefreshTokenCookie(httpContext, result.Value.RawRefreshToken);
    return Results.Created("/api/v1/auth/me", result.Value);
});

authGroup.MapPost("/login", async (LoginRequest request, IAuthService authService, HttpContext httpContext, CancellationToken ct) =>
{
    Result<AuthResponse> result = await authService.LoginAsync(request, ct);

    if (result.IsFailure)
        return Results.Problem(result.Error.Description, statusCode: 401);

    SetRefreshTokenCookie(httpContext, result.Value.RawRefreshToken);
    return Results.Ok(result.Value);
});

authGroup.MapPost("/refresh", async (IAuthService authService, HttpContext httpContext, CancellationToken ct) =>
{
    string? refreshToken = httpContext.Request.Cookies["refreshToken"];

    if (string.IsNullOrEmpty(refreshToken))
        return Results.Problem("No refresh token provided.", statusCode: 401);

    Result<AuthResponse> result = await authService.RefreshAsync(refreshToken, ct);

    if (result.IsFailure)
        return Results.Problem(result.Error.Description, statusCode: 401);

    SetRefreshTokenCookie(httpContext, result.Value.RawRefreshToken);
    return Results.Ok(result.Value);
});

authGroup.MapPost("/logout", async (IAuthService authService, HttpContext httpContext, CancellationToken ct) =>
{
    string? userIdClaim = httpContext.User.FindFirst("sub")?.Value;

    if (!Guid.TryParse(userIdClaim, out Guid userId))
        return Results.Problem("Not authenticated.", statusCode: 401);

    await authService.LogoutAsync(userId, ct);

    bool isSecure = !string.Equals(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        "Development",
        StringComparison.OrdinalIgnoreCase);

    httpContext.Response.Cookies.Delete("refreshToken", new CookieOptions
    {
        HttpOnly = true,
        Secure = isSecure,
        SameSite = isSecure ? SameSiteMode.Strict : SameSiteMode.Lax,
        Path = "/api/v1/auth"
    });

    return Results.Ok(new { message = "Logged out." });
}).RequireAuthorization();

// ---------------------------------------------------------------------------
// Customer Endpoints — /api/v1/customers
// ---------------------------------------------------------------------------
RouteGroupBuilder customerGroup = app.MapGroup("/api/v1/customers").RequireAuthorization("Staff");

customerGroup.MapGet("/", async (
    string? search, string? tag, ConsentStatus? consentStatus, decimal? noShowScoreGte,
    int? page, int? pageSize,
    ICustomerService customerService, CancellationToken ct) =>
{
    var query = new CustomerQuery(search, tag, consentStatus, noShowScoreGte, page ?? 1, pageSize ?? 25);
    Result<PagedResult<CustomerDto>> result = await customerService.ListAsync(query, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(result.Value);
});

customerGroup.MapPost("/", async (CreateCustomerRequest request, ICustomerService customerService, CancellationToken ct) =>
{
    Result<CustomerDto> result = await customerService.CreateAsync(request, ct);

    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Customer.PhoneTaken" => 409,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }

    return Results.Created($"/api/v1/customers/{result.Value.Id}", result.Value);
});

customerGroup.MapGet("/{id:guid}", async (Guid id, ICustomerService customerService, CancellationToken ct) =>
{
    Result<CustomerDto> result = await customerService.GetByIdAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

customerGroup.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, ICustomerService customerService, CancellationToken ct) =>
{
    Result<CustomerDto> result = await customerService.UpdateAsync(id, request, ct);

    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Customer.NotFound" => 404,
            "Customer.PhoneTaken" => 409,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }

    return Results.Ok(result.Value);
});

customerGroup.MapDelete("/{id:guid}", async (Guid id, ICustomerService customerService, CancellationToken ct) =>
{
    Result result = await customerService.DeleteAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(new { message = "Customer anonymized and deleted." });
});

customerGroup.MapGet("/{id:guid}/history", async (Guid id, ICustomerService customerService, CancellationToken ct) =>
{
    Result<List<ConsentRecordDto>> result = await customerService.GetConsentHistoryAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

customerGroup.MapPut("/{id:guid}/consent", async (Guid id, UpdateConsentRequest request, ICustomerService customerService, CancellationToken ct) =>
{
    Result<CustomerDto> result = await customerService.UpdateConsentAsync(id, request, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

customerGroup.MapPost("/import", async (HttpRequest httpRequest, ICustomerService customerService, CancellationToken ct) =>
{
    if (!httpRequest.HasFormContentType)
        return Results.Problem("Expected multipart/form-data with a CSV file.", statusCode: 400);

    IFormFile? file = httpRequest.Form.Files.GetFile("file");
    if (file is null || file.Length == 0)
        return Results.Problem("No file uploaded. Include a 'file' field with a CSV.", statusCode: 400);

    await using Stream stream = file.OpenReadStream();
    Result<CsvImportResult> result = await customerService.ImportCsvAsync(stream, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(result.Value);
}).DisableAntiforgery();

// ---------------------------------------------------------------------------
// Health check
// ---------------------------------------------------------------------------
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/// <summary>
/// Sets the raw refresh token as an httpOnly, Secure, SameSite=Strict cookie.
/// Secure flag is disabled in Development to allow HTTP testing.
/// </summary>
static void SetRefreshTokenCookie(HttpContext httpContext, string rawRefreshToken)
{
    bool isSecure = !string.Equals(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        "Development",
        StringComparison.OrdinalIgnoreCase);

    httpContext.Response.Cookies.Append("refreshToken", rawRefreshToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = isSecure,
        SameSite = isSecure ? SameSiteMode.Strict : SameSiteMode.Lax,
        Path = "/api/v1/auth",
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });
}

// To enable integration tests to reference Program
public partial class Program { }
