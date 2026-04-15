using System.Text;
using System.Text.Json.Serialization;
using System.ClientModel;
using Azure.AI.OpenAI;
using FlowPilot.Api.Hubs;
using FlowPilot.Api.Middleware;
using FlowPilot.Api.Services;
using FlowPilot.Application.Agents;
using FlowPilot.Application.Auth;
using FlowPilot.Application.Appointments;
using FlowPilot.Application.Customers;
using FlowPilot.Application.Messaging;
using FlowPilot.Application.Realtime;
using FlowPilot.Application.Services;
using FlowPilot.Application.PublicBooking;
using FlowPilot.Application.Settings;
using FlowPilot.Application.Stats;
using FlowPilot.Application.Templates;
using FlowPilot.Api.Filters;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Agents;
using FlowPilot.Infrastructure.Agents.Tools;
using FlowPilot.Infrastructure.Auth;
using FlowPilot.Infrastructure.Appointments;
using FlowPilot.Infrastructure.Customers;
using FlowPilot.Infrastructure.PublicBooking;
using FlowPilot.Infrastructure.Services;
using FlowPilot.Infrastructure.Settings;
using FlowPilot.Infrastructure.Stats;
using FlowPilot.Infrastructure.Messaging;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Infrastructure.Realtime;
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
builder.Services.AddScoped<ITenantSettingsService, TenantSettingsService>();
builder.Services.AddScoped<IDashboardStatsService, DashboardStatsService>();
builder.Services.AddScoped<IPublicBookingService, PublicBookingService>();

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
// MediatR — in-process domain events (scan both Infrastructure and Api assemblies)
// ---------------------------------------------------------------------------
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<AppointmentStatusChangedHandler>();
    cfg.RegisterServicesFromAssemblyContaining<Program>();
});

// ---------------------------------------------------------------------------
// SignalR — real-time appointment + SMS updates
// ---------------------------------------------------------------------------
builder.Services.AddSignalR();

// Realtime fan-out via Postgres LISTEN/NOTIFY: the notifier is used by MediatR bridge
// handlers in Infrastructure (shared by API and Workers), the listener runs only in the
// API and relays notifications into the SignalR hubs.
builder.Services.AddScoped<IRealtimeNotifier, PostgresRealtimeNotifier>();
builder.Services.AddHostedService<PostgresRealtimeListener>();

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
    registry.Register(sp.GetRequiredService<CancelAppointmentTool>());
    registry.Register(sp.GetRequiredService<SendRescheduleLinkTool>());
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
builder.Services.AddScoped<CancelAppointmentTool>();
builder.Services.AddScoped<SendRescheduleLinkTool>();
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

    // SignalR sends JWT via query string for WebSocket connections
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            string? token = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token)
                && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Token = token;
            }
            return Task.CompletedTask;
        }
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
app.UseMiddleware<PublicTenantMiddleware>();
app.MapHub<AppointmentHub>("/hubs/appointments");
app.MapHub<SmsHub>("/hubs/sms");

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
    DateTime? dateFrom, DateTime? dateTo, string? search, bool? atRisk, int? page, int? pageSize,
    IAppointmentService appointmentService, CancellationToken ct) =>
{
    var query = new AppointmentQuery(status, staffUserId, customerId, dateFrom, dateTo, search, atRisk, page ?? 1, pageSize ?? 25);
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
// Twilio Webhooks — /api/webhooks/twilio (unauthenticated, tenant resolved from To phone)
// ---------------------------------------------------------------------------
RouteGroupBuilder twilioGroup = app.MapGroup("/api/webhooks/twilio")
    .AllowAnonymous()
    .AddEndpointFilter<TwilioSignatureFilter>();

twilioGroup.MapPost("/sms/inbound", async (HttpRequest request, AppDbContext db, IMessagingService messagingService, CancellationToken ct) =>
{
    // Twilio sends application/x-www-form-urlencoded
    IFormCollection form = await request.ReadFormAsync(ct);
    string messageSid = form["MessageSid"].ToString();
    string from = form["From"].ToString();
    string to = form["To"].ToString();
    string body = form["Body"].ToString();

    if (string.IsNullOrEmpty(messageSid) || string.IsNullOrEmpty(from))
        return Results.BadRequest(new { error = "Missing required Twilio fields." });

    // Resolve tenant from the To phone (business's Twilio number)
    TenantSettings? settings = await db.TenantSettings
        .IgnoreQueryFilters() // No tenant context — resolving tenant from phone number
        .FirstOrDefaultAsync(s => s.DefaultSenderPhone == to && !s.IsDeleted, ct);

    if (settings is null)
        return Results.NotFound(new { error = $"No tenant found for phone {to}." });

    // Set tenant context so downstream services scope queries correctly
    request.HttpContext.Items["PublicTenantId"] = settings.OwnerTenantId;

    Result result = await messagingService.ProcessInboundAsync(
        new InboundSmsWebhook(messageSid, from, to, body), ct);

    // Twilio expects a 200 with TwiML (empty response = no reply SMS)
    return Results.Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>", "application/xml");
});

twilioGroup.MapPost("/sms/status", async (HttpRequest request, AppDbContext db, IMessagingService messagingService, CancellationToken ct) =>
{
    IFormCollection form = await request.ReadFormAsync(ct);
    string messageSid = form["MessageSid"].ToString();
    string status = form["MessageStatus"].ToString();
    string to = form["To"].ToString();

    if (string.IsNullOrEmpty(messageSid) || string.IsNullOrEmpty(status))
        return Results.BadRequest(new { error = "Missing required Twilio fields." });

    // Resolve tenant from To phone
    TenantSettings? settings = await db.TenantSettings
        .IgnoreQueryFilters() // No tenant context — resolving tenant from phone number
        .FirstOrDefaultAsync(s => s.DefaultSenderPhone == to && !s.IsDeleted, ct);

    if (settings is not null)
        request.HttpContext.Items["PublicTenantId"] = settings.OwnerTenantId;

    Result result = await messagingService.ProcessDeliveryStatusAsync(
        new DeliveryStatusWebhook(messageSid, status), ct);

    return Results.Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>", "application/xml");
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

messagingGroup.MapGet("/conversations", async (
    string? search, int? page, int? pageSize,
    IMessagingService messagingService, CancellationToken ct) =>
{
    Result<PagedResult<ConversationSummaryDto>> result = await messagingService.GetConversationsAsync(
        search, page ?? 1, pageSize ?? 25, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(result.Value);
});

messagingGroup.MapGet("/conversations/{customerId:guid}/messages", async (
    Guid customerId, int? page, int? pageSize,
    IMessagingService messagingService, CancellationToken ct) =>
{
    Result<PagedResult<MessageDto>> result = await messagingService.GetMessagesAsync(
        customerId, page ?? 1, pageSize ?? 50, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: result.Error.Code.Contains("NotFound") ? 404 : 400)
        : Results.Ok(result.Value);
});

messagingGroup.MapPost("/conversations/{customerId:guid}/send", async (
    Guid customerId, SendManualSmsRequest body,
    IMessagingService messagingService, CancellationToken ct) =>
{
    Result<SendSmsResponse> result = await messagingService.SendRawAsync(
        new SendRawSmsRequest(customerId, body.Body), ct);

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
// Tenant Settings Endpoints — /api/v1/settings
// ---------------------------------------------------------------------------
RouteGroupBuilder settingsGroup = app.MapGroup("/api/v1/settings").RequireAuthorization("Staff");

settingsGroup.MapGet("/", async (ITenantSettingsService settingsService, CancellationToken ct) =>
{
    Result<TenantSettingsDto> result = await settingsService.GetAsync(ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

settingsGroup.MapPut("/", async (UpdateTenantSettingsRequest request, ITenantSettingsService settingsService, CancellationToken ct) =>
{
    Result<TenantSettingsDto> result = await settingsService.UpdateAsync(request, ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

// ---------------------------------------------------------------------------
// Dashboard Stats Endpoints — /api/v1/stats
// ---------------------------------------------------------------------------
RouteGroupBuilder statsGroup = app.MapGroup("/api/v1/stats").RequireAuthorization("Staff");

statsGroup.MapGet("/dashboard", async (IDashboardStatsService statsService, CancellationToken ct) =>
{
    Result<DashboardStatsDto> result = await statsService.GetDashboardStatsAsync(ct);

    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(result.Value);
});

// ---------------------------------------------------------------------------
// Public Booking Endpoints — /api/v1/public/book/{slug}
// No authentication required. Tenant resolved from slug by PublicTenantMiddleware.
// ---------------------------------------------------------------------------
RouteGroupBuilder publicBookingGroup = app.MapGroup("/api/v1/public/book/{slug}");

publicBookingGroup.MapGet("/", async (string slug, IPublicBookingService bookingService, CancellationToken ct) =>
{
    Result<PublicBusinessInfoDto> result = await bookingService.GetBusinessInfoAsync(ct);
    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 404)
        : Results.Ok(result.Value);
});

publicBookingGroup.MapGet("/slots", async (string slug, string date, Guid serviceId, IPublicBookingService bookingService, CancellationToken ct) =>
{
    if (!DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime parsedDate))
        return Results.Problem("Invalid date format. Use yyyy-MM-dd.", statusCode: 400);

    Result<List<TimeSlotDto>> result = await bookingService.GetAvailableSlotsAsync(parsedDate, serviceId, ct);
    return result.IsFailure
        ? Results.Problem(result.Error.Description, statusCode: 400)
        : Results.Ok(result.Value);
});

publicBookingGroup.MapPost("/", async (string slug, PublicBookingRequest request, IPublicBookingService bookingService, CancellationToken ct) =>
{
    Result<PublicBookingConfirmationDto> result = await bookingService.BookAsync(request, ct);
    if (result.IsFailure)
    {
        int status = result.Error.Code switch
        {
            "Service.NotFound" => 404,
            "Booking.SlotUnavailable" => 409,
            "Customer.InvalidPhone" => 400,
            _ => 400
        };
        return Results.Problem(result.Error.Description, statusCode: status);
    }
    return Results.Created($"/api/v1/public/book/{slug}/confirmation/{result.Value.AppointmentId}", result.Value);
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
