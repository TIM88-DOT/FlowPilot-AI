using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowPilot.Application.Appointments;
using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlowPilot.IntegrationTests;

/// <summary>
/// Integration tests for Workstream G (pre-appointment escalation), exercising the extracted
/// IAppointmentLifecycleService and IReminderDispatchService against a real test database:
/// - At-risk flagging fires exactly once per appointment (idempotency guard).
/// - The second (T−3h) reminder is Cancelled instead of sent once the customer confirms.
/// </summary>
public class AppointmentLifecycleTests : IClassFixture<FlowPilotApiFactory>
{
    private readonly FlowPilotApiFactory _factory;
    private readonly HttpClient _client;
    private const string AuthBase = "/api/v1/auth";
    private const string AppointmentsBase = "/api/v1/appointments";
    private const string CustomersBase = "/api/v1/customers";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppointmentLifecycleTests(FlowPilotApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
    }

    private record AuthResponseDto(string AccessToken, DateTime ExpiresAt, UserInfoDto User);
    private record UserInfoDto(Guid Id, Guid TenantId, string Email, string FirstName, string LastName, string Role, string BusinessName);
    private record CustomerDto(Guid Id, string Phone, string? Email, string FirstName, string? LastName,
        string PreferredLanguage, string? Tags, decimal NoShowScore, string ConsentStatus, DateTime CreatedAt, DateTime UpdatedAt);
    private record AppointmentDto(Guid Id, Guid CustomerId, string CustomerName, Guid? StaffUserId, string? ExternalId,
        string Status, DateTime StartsAt, DateTime EndsAt, string? ServiceName, string? Notes, DateTime CreatedAt, DateTime UpdatedAt);

    private async Task<Guid> AuthenticateAsync(string email)
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync($"{AuthBase}/register", new
        {
            email,
            password = "Test1234!@#",
            firstName = "Alex",
            lastName = "Tremblay",
            businessName = "Salon Prestige",
            businessPhone = "+14165551234",
            timezone = "America/Toronto",
            defaultLanguage = "fr"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        AuthResponseDto? auth = await http.Content.ReadFromJsonAsync<AuthResponseDto>(JsonOptions);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return auth.User.TenantId;
    }

    private async Task<CustomerDto> CreateCustomerAsync(string phone, string firstName)
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(CustomersBase, new
        {
            phone,
            firstName,
            lastName = "Lavoie",
            email = $"{firstName.ToLower()}@test.dev",
            preferredLanguage = "fr",
            consentSource = "Manual"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions))!;
    }

    private async Task<AppointmentDto> CreateAppointmentAsync(Guid customerId, DateTime startsAt)
    {
        HttpResponseMessage http = await _client.PostAsJsonAsync(AppointmentsBase, new
        {
            customerId,
            startsAt,
            endsAt = startsAt.AddHours(1),
            serviceName = "Haircut"
        });

        Assert.Equal(HttpStatusCode.Created, http.StatusCode);
        return (await http.Content.ReadFromJsonAsync<AppointmentDto>(JsonOptions))!;
    }

    // =====================================================================
    // AT-RISK FLAGGING — fires exactly once
    // =====================================================================

    [Fact]
    public async Task ScanAtRisk_RunTwice_FlagsOnceAndPublishesEventOnce()
    {
        Guid tenantId = await AuthenticateAsync("atrisk-once@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+14167000001", "Yasmine");

        // Scheduled appointment one day out — never confirmed.
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id, DateTime.UtcNow.AddDays(1));

        using IServiceScope scope = _factory.Services.CreateScope();
        IAppointmentLifecycleService lifecycle = scope.ServiceProvider.GetRequiredService<IAppointmentLifecycleService>();
        AtRiskEventSpy spy = scope.ServiceProvider.GetRequiredService<AtRiskEventSpy>();

        // A 2-day window puts the appointment squarely inside the at-risk window.
        TimeSpan window = TimeSpan.FromDays(2);
        await lifecycle.ScanAtRiskAsync(window);
        DateTime? firstAlertedAt = await ReadAtRiskAlertedAtAsync(appt.Id);

        // Second scan must be a no-op for this appointment (idempotency guard).
        await lifecycle.ScanAtRiskAsync(window);
        DateTime? secondAlertedAt = await ReadAtRiskAlertedAtAsync(appt.Id);

        Assert.NotNull(firstAlertedAt);                       // flagged on first run
        Assert.Equal(firstAlertedAt, secondAlertedAt);        // not re-stamped on second run
        Assert.Equal(1, spy.CountFor(appt.Id));               // event published exactly once
    }

    // =====================================================================
    // REMINDER CANCELLATION — second reminder skipped once confirmed
    // =====================================================================

    [Fact]
    public async Task DispatchDue_AfterConfirm_CancelsReminderInsteadOfSending()
    {
        Guid tenantId = await AuthenticateAsync("reminder-cancel@test.dev");
        CustomerDto customer = await CreateCustomerAsync("+14167000002", "Mehdi");
        AppointmentDto appt = await CreateAppointmentAsync(customer.Id, DateTime.UtcNow.AddHours(3));

        // Seed an opted-in customer + a due Pending reminder linked to the appointment.
        Guid messageId = await SeedDueReminderAsync(tenantId, customer.Id, appt.Id);

        // Customer confirms early — the appointment leaves Scheduled state.
        HttpResponseMessage confirm = await _client.PostAsync($"{AppointmentsBase}/{appt.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IReminderDispatchService dispatch = scope.ServiceProvider.GetRequiredService<IReminderDispatchService>();
            await dispatch.DispatchDueAsync();
        }

        // The reminder must be Cancelled, never sent — and no outbound Message logged.
        using IServiceScope assertScope = _factory.Services.CreateScope();
        AppDbContext db = assertScope.ServiceProvider.GetRequiredService<AppDbContext>();

        ScheduledMessage message = await db.ScheduledMessages
            .IgnoreQueryFilters()
            .SingleAsync(m => m.Id == messageId);

        Assert.Equal(ScheduledMessageStatus.Cancelled, message.Status);
        Assert.Null(message.SentAt);

        bool anyOutbound = await db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.CustomerId == customer.Id && m.Direction == MessageDirection.Outbound);
        Assert.False(anyOutbound);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<DateTime?> ReadAtRiskAlertedAtAsync(Guid appointmentId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Appointment appointment = await db.Appointments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(a => a.Id == appointmentId);
        return appointment.AtRiskAlertedAt;
    }

    private async Task<Guid> SeedDueReminderAsync(Guid tenantId, Guid customerId, Guid appointmentId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Force consent to OptedIn so the dispatcher's cancellation is provably due to the
        // appointment leaving Scheduled state, not the consent gate.
        Customer customer = await db.Customers
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == customerId);
        customer.ConsentStatus = ConsentStatus.OptedIn;

        ScheduledMessage message = new()
        {
            TenantId = tenantId,
            AppointmentId = appointmentId,
            CustomerId = customerId,
            Status = ScheduledMessageStatus.Pending,
            ScheduledAt = DateTime.UtcNow.AddMinutes(-5), // due
            RenderedBody = "Dernière chance : confirmez votre rendez-vous."
        };
        db.ScheduledMessages.Add(message);

        await db.SaveChangesAsync();
        return message.Id;
    }
}
