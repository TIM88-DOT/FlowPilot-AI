using System.Text;
using FlowPilot.Api.Services;
using FlowPilot.Application.Auth;
using FlowPilot.Infrastructure.Auth;
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

    httpContext.Response.Cookies.Delete("refreshToken", new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth"
    });

    return Results.Ok(new { message = "Logged out." });
}).RequireAuthorization();

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
/// </summary>
static void SetRefreshTokenCookie(HttpContext httpContext, string rawRefreshToken)
{
    httpContext.Response.Cookies.Append("refreshToken", rawRefreshToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/v1/auth",
        Expires = DateTimeOffset.UtcNow.AddDays(7)
    });
}

// To enable integration tests to reference Program
public partial class Program { }
