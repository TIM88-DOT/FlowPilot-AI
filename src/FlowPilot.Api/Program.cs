using System.Text;
using System.Text.Json.Serialization;
using System.ClientModel;
using Azure.AI.OpenAI;
using FlowPilot.Api.Services;
using FlowPilot.Application.Agents;
using FlowPilot.Application.Auth;
using FlowPilot.Application.Appointments;
using FlowPilot.Application.Customers;
using FlowPilot.Application.Messaging;
using FlowPilot.Application.Services;
using FlowPilot.Application.Templates;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Agents;
using FlowPilot.Infrastructure.Agents.Tools;
using FlowPilot.Infrastructure.Auth;
using FlowPilot.Infrastructure.Appointments;
using FlowPilot.Infrastructure.Customers;
using FlowPilot.Infrastructure.Services;
using FlowPilot.Infrastructure.Messaging;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Infrastructure.Templates;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using OpenAI.Chat;
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
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IServiceService, ServiceService>();

// ---------------------------------------------------------------------------
// Messaging services — ISmsProvider swapped by config: "Fake" (dev) or "Twilio" (prod)
// ---------------------------------------------------------------------------
string smsProvider = builder.Configuration["SmsProvider"] ?? "Fake";
if (smsProvider.Equals("Twilio", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<ISmsProvider, TwilioSmsProvider>();
else
    builder.Services.AddScoped<ISmsProvider, FakeSmsProvider>();
builder.Services.AddScoped<ITemplateRenderer, TemplateRenderer>();
builder.Services.AddScoped<IMessagingService, MessagingService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();

// ---------------------------------------------------------------------------
// MediatR — in-process domain events
// ---------------------------------------------------------------------------
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AppointmentStatusChangedHandler>());

// ---------------------------------------------------------------------------
// AI Agents — Azure OpenAI + Tool Registry + Orchestrator
// ---------------------------------------------------------------------------
builder.Services.Configure<AzureOpenAISettings>(builder.Configuration.GetSection(AzureOpenAISettings.SectionName));

string aoaiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"] ?? "";
string aoaiApiKey = builder.Configuration["AzureOpenAI:ApiKey"] ?? "";
string aoaiDeployment = builder.Configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o";

if (!string.IsNullOrEmpty(aoaiEndpoint) && !string.IsNullOrEmpty(aoaiApiKey))
{
    var openAIClient = new AzureOpenAIClient(
        new Uri(aoaiEndpoint),
        new ApiKeyCredential(aoaiApiKey));
    builder.Services.AddSingleton(openAIClient.GetChatClient(aoaiDeployment));
}
// When Azure OpenAI is not configured (dev/test), no ChatClient is registered.
// AgentOrchestrator handles this gracefully by checking if it was injected.

// Tool registry + agent tools (scoped — tools depend on AppDbContext)
builder.Services.AddScoped<IToolRegistry>(sp =>
{
    var registry = new ToolRegistry();
    registry.Register(sp.GetRequiredService<GetCustomerHistoryTool>());
    registry.Register(sp.GetRequiredService<GetAppointmentDetailsTool>());
    registry.Register(sp.GetRequiredService<ScheduleSmsTool>());
    registry.Register(sp.GetRequiredService<SendSmsTool>());
    registry.Register(sp.GetRequiredService<ConfirmAppointmentTool>());
    registry.Register(sp.GetRequiredService<ClassifyIntentTool>());
    registry.Register(sp.GetRequiredService<GetReviewPlatformsTool>());
    registry.Register(sp.GetRequiredService<CheckReviewCooldownTool>());
    return registry;
});

builder.Services.AddScoped<GetCustomerHistoryTool>();
builder.Services.AddScoped<GetAppointmentDetailsTool>();
builder.Services.AddScoped<ScheduleSmsTool>();
builder.Services.AddScoped<SendSmsTool>();
builder.Services.AddScoped<ConfirmAppointmentTool>();
builder.Services.AddScoped<ClassifyIntentTool>();
builder.Services.AddScoped<GetReviewPlatformsTool>();
builder.Services.AddScoped<CheckReviewCooldownTool>();

// Orchestrator + agents
builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
builder.Services.AddScoped<ReplyHandlingAgent>();

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
// Appointment Endpoints — /api/v1/appointments
// ---------------------------------------------------------------------------
RouteGroupBuilder appointmentGroup = app.MapGroup("/api/v1/appointments").RequireAuthorization("Staff");

appointmentGroup.MapGet("/", async (
    AppointmentStatus? status, Guid? staffUserId, Guid? customerId,
    DateTime? dateFrom, DateTime? dateTo, int? page, int? pageSize,
    IAppointmentService appointmentService, CancellationToken ct) =>
{
    var query = new AppointmentQuery(status, staffUserId, customerId, dateFrom, dateTo, page ?? 1, pageSize ?? 25);
    Result<PagedResult<AppointmentDto>> result = await appointmentService.ListAsync(query, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(result.Value);
});

appointmentGroup.MapPost("/", async (CreateAppointmentRequest request, IAppointmentService appointmentService, CancellationToken ct) =>
{
    Result<AppointmentDto> result = await appointmentService.CreateAsync(request, ct);

    if (result.IsFailure)
        return Results.Problem(result.Error.Description, statusCode: 400);

    return Results.Created($"/api/v1/appointments/{result.Value.Id}", result.Value);
});

appointmentGroup.MapGet("/{id:guid}", async (Guid id, IAppointmentService appointmentService, CancellationToken ct) =>
{
    Result<AppointmentDto> result = await appointmentService.GetByIdAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

appointmentGroup.MapPost("/{id:guid}/confirm", async (Guid id, IAppointmentService appointmentService, CancellationToken ct) =>
{
    Result<AppointmentDto> result = await appointmentService.ConfirmAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: result.Error.Code == "Appointment.NotFound" ? 404 : 400)
        : Results.Ok(result.Value);
});

appointmentGroup.MapPost("/{id:guid}/cancel", async (Guid id, IAppointmentService appointmentService, CancellationToken ct) =>
{
    Result<AppointmentDto> result = await appointmentService.CancelAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: result.Error.Code == "Appointment.NotFound" ? 404 : 400)
        : Results.Ok(result.Value);
});

appointmentGroup.MapPost("/{id:guid}/complete", async (Guid id, IAppointmentService appointmentService, CancellationToken ct) =>
{
    Result<AppointmentDto> result = await appointmentService.CompleteAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: result.Error.Code == "Appointment.NotFound" ? 404 : 400)
        : Results.Ok(result.Value);
});

appointmentGroup.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleAppointmentRequest request, IAppointmentService appointmentService, CancellationToken ct) =>
{
    Result<AppointmentDto> result = await appointmentService.RescheduleAsync(id, request, ct);

    if (result.IsFailure)
    {
        int statusCode = result.Error.Code switch
        {
            "Appointment.NotFound" => 404,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: statusCode);
    }

    return Results.Ok(result.Value);
});

// ---------------------------------------------------------------------------
// Service Endpoints — /api/v1/services
// ---------------------------------------------------------------------------
RouteGroupBuilder serviceGroup = app.MapGroup("/api/v1/services").RequireAuthorization("Staff");

serviceGroup.MapGet("/", async (IServiceService serviceService, CancellationToken ct) =>
{
    Result<List<ServiceDto>> result = await serviceService.ListAsync(ct);
    return Results.Ok(result.Value);
});

serviceGroup.MapGet("/{id:guid}", async (Guid id, IServiceService serviceService, CancellationToken ct) =>
{
    Result<ServiceDto> result = await serviceService.GetByIdAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

serviceGroup.MapPost("/", async (CreateServiceRequest request, IServiceService serviceService, CancellationToken ct) =>
{
    Result<ServiceDto> result = await serviceService.CreateAsync(request, ct);

    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Service.NameTaken" => 409,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }

    return Results.Created($"/api/v1/services/{result.Value.Id}", result.Value);
});

serviceGroup.MapPut("/{id:guid}", async (Guid id, UpdateServiceRequest request, IServiceService serviceService, CancellationToken ct) =>
{
    Result<ServiceDto> result = await serviceService.UpdateAsync(id, request, ct);

    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Service.NotFound" => 404,
            "Service.NameTaken" => 409,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }

    return Results.Ok(result.Value);
});

serviceGroup.MapDelete("/{id:guid}", async (Guid id, IServiceService serviceService, CancellationToken ct) =>
{
    Result result = await serviceService.DeleteAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(new { message = "Service deleted." });
});

// ---------------------------------------------------------------------------
// Webhook Endpoints — /api/webhooks
// ---------------------------------------------------------------------------
RouteGroupBuilder webhookGroup = app.MapGroup("/api/webhooks").RequireAuthorization("Staff");

webhookGroup.MapPost("/appointments/inbound", async (InboundAppointmentWebhook webhook, IAppointmentService appointmentService, CancellationToken ct) =>
{
    Result<AppointmentDto> result = await appointmentService.IngestFromWebhookAsync(webhook, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(result.Value);
});

webhookGroup.MapPost("/sms/inbound", async (InboundSmsWebhook webhook, IMessagingService messagingService, CancellationToken ct) =>
{
    Result result = await messagingService.ProcessInboundAsync(webhook, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(new { message = "Processed." });
});

webhookGroup.MapPost("/sms/status", async (DeliveryStatusWebhook webhook, IMessagingService messagingService, CancellationToken ct) =>
{
    Result result = await messagingService.ProcessDeliveryStatusAsync(webhook, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(new { message = "Status updated." });
});

// ---------------------------------------------------------------------------
// Messaging Endpoints — /api/v1/messaging
// ---------------------------------------------------------------------------
RouteGroupBuilder messagingGroup = app.MapGroup("/api/v1/messaging").RequireAuthorization("Staff");

messagingGroup.MapPost("/send", async (SendSmsRequest request, IMessagingService messagingService, CancellationToken ct) =>
{
    Result<SendSmsResponse> result = await messagingService.SendTemplatedAsync(request, ct);

    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Messaging.ConsentRequired" => 403,
            "Customer.NotFound" => 404,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }

    return Results.Ok(result.Value);
});

messagingGroup.MapPost("/send-raw", async (SendRawSmsRequest request, IMessagingService messagingService, CancellationToken ct) =>
{
    Result<SendSmsResponse> result = await messagingService.SendRawAsync(request, ct);

    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Messaging.ConsentRequired" => 403,
            "Customer.NotFound" => 404,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }

    return Results.Ok(result.Value);
});

// ---------------------------------------------------------------------------
// Template Endpoints — /api/v1/templates
// ---------------------------------------------------------------------------
RouteGroupBuilder templateGroup = app.MapGroup("/api/v1/templates").RequireAuthorization("Staff");

templateGroup.MapGet("/", async (ITemplateService templateService, CancellationToken ct) =>
{
    Result<List<TemplateDto>> result = await templateService.ListAsync(ct);
    return Results.Ok(result.Value);
});

templateGroup.MapGet("/{id:guid}", async (Guid id, ITemplateService templateService, CancellationToken ct) =>
{
    Result<TemplateDto> result = await templateService.GetByIdAsync(id, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

templateGroup.MapPost("/", async (CreateTemplateRequest request, ITemplateService templateService, CancellationToken ct) =>
{
    Result<TemplateDto> result = await templateService.CreateAsync(request, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Created($"/api/v1/templates/{result.Value.Id}", result.Value);
});

templateGroup.MapPut("/{id:guid}", async (Guid id, UpdateTemplateRequest request, ITemplateService templateService, CancellationToken ct) =>
{
    Result<TemplateDto> result = await templateService.UpdateAsync(id, request, ct);

    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Template.NotFound" => 404,
            "Template.SystemReadOnly" => 403,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }

    return Results.Ok(result.Value);
});

templateGroup.MapDelete("/{id:guid}", async (Guid id, ITemplateService templateService, CancellationToken ct) =>
{
    Result result = await templateService.DeleteAsync(id, ct);

    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Template.NotFound" => 404,
            "Template.SystemReadOnly" => 403,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }

    return Results.Ok(new { message = "Template deleted." });
});

templateGroup.MapPut("/{id:guid}/variants", async (Guid id, UpsertLocaleVariantRequest request, ITemplateService templateService, CancellationToken ct) =>
{
    Result<TemplateDto> result = await templateService.UpsertLocaleVariantAsync(id, request, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

templateGroup.MapDelete("/{id:guid}/variants/{locale}", async (Guid id, string locale, ITemplateService templateService, CancellationToken ct) =>
{
    Result result = await templateService.DeleteLocaleVariantAsync(id, locale, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(new { message = "Locale variant deleted." });
});

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
