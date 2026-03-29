using FlowPilot.Application.Appointments;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Appointments;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using MediatR;
using NSubstitute;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests appointment status transition state machine and time validation.
/// </summary>
public sealed class AppointmentTransitionTests : IDisposable
{
    private readonly TestDbFixture _fixture = new();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly AppointmentService _sut;
    private readonly AppDbContext _db;

    public AppointmentTransitionTests()
    {
        _db = _fixture.CreateContext();
        _sut = new AppointmentService(_db, _fixture.CurrentTenant, _mediator);
    }

    public void Dispose()
    {
        _db.Dispose();
        _fixture.Dispose();
    }

    private async Task<Customer> SeedCustomerAsync()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            Phone = "+213555000001",
            FirstName = "Test",
            ConsentStatus = ConsentStatus.OptedIn
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    private async Task<Appointment> SeedAppointmentAsync(Customer customer, AppointmentStatus status = AppointmentStatus.Scheduled)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            CustomerId = customer.Id,
            Status = status,
            StartsAt = DateTime.UtcNow.AddHours(2),
            EndsAt = DateTime.UtcNow.AddHours(3),
            ServiceName = "Haircut"
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    // -----------------------------------------------------------------------
    // Valid transitions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Scheduled_To_Confirmed_Succeeds()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Scheduled);

        Result<AppointmentDto> result = await _sut.ConfirmAsync(appointment.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("Confirmed", result.Value.Status);
    }

    [Fact]
    public async Task Scheduled_To_Cancelled_Succeeds()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Scheduled);

        Result<AppointmentDto> result = await _sut.CancelAsync(appointment.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cancelled", result.Value.Status);
    }

    [Fact]
    public async Task Confirmed_To_Completed_Succeeds()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Confirmed);

        Result<AppointmentDto> result = await _sut.CompleteAsync(appointment.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("Completed", result.Value.Status);
    }

    // -----------------------------------------------------------------------
    // Invalid transitions (terminal states)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Missed)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Rescheduled)]
    public async Task TerminalState_To_Confirmed_Fails(AppointmentStatus terminalStatus)
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, terminalStatus);

        Result<AppointmentDto> result = await _sut.ConfirmAsync(appointment.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Appointment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public async Task Scheduled_To_Completed_Fails()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Scheduled);

        Result<AppointmentDto> result = await _sut.CompleteAsync(appointment.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("Appointment.InvalidTransition", result.Error.Code);
        Assert.Contains("Scheduled", result.Error.Description);
        Assert.Contains("Completed", result.Error.Description);
    }

    // -----------------------------------------------------------------------
    // Time range validation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_EndsBeforeStarts_Fails()
    {
        Customer customer = await SeedCustomerAsync();
        DateTime now = DateTime.UtcNow;

        var request = new CreateAppointmentRequest(
            CustomerId: customer.Id,
            StartsAt: now.AddHours(3),
            EndsAt: now.AddHours(1));

        Result<AppointmentDto> result = await _sut.CreateAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Appointment.InvalidTimeRange", result.Error.Code);
    }

    [Fact]
    public async Task Create_EndsEqualsStarts_Fails()
    {
        Customer customer = await SeedCustomerAsync();
        DateTime now = DateTime.UtcNow.AddHours(1);

        var request = new CreateAppointmentRequest(
            CustomerId: customer.Id,
            StartsAt: now,
            EndsAt: now);

        Result<AppointmentDto> result = await _sut.CreateAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Appointment.InvalidTimeRange", result.Error.Code);
    }

    [Fact]
    public async Task Create_ValidTimes_Succeeds()
    {
        Customer customer = await SeedCustomerAsync();
        DateTime now = DateTime.UtcNow;

        var request = new CreateAppointmentRequest(
            CustomerId: customer.Id,
            StartsAt: now.AddHours(1),
            EndsAt: now.AddHours(2),
            ServiceName: "Haircut");

        Result<AppointmentDto> result = await _sut.CreateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("Scheduled", result.Value.Status);
        Assert.Equal("Haircut", result.Value.ServiceName);
    }

    // -----------------------------------------------------------------------
    // Customer not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_NonexistentCustomer_Fails()
    {
        var request = new CreateAppointmentRequest(
            CustomerId: Guid.NewGuid(),
            StartsAt: DateTime.UtcNow.AddHours(1),
            EndsAt: DateTime.UtcNow.AddHours(2));

        Result<AppointmentDto> result = await _sut.CreateAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Appointment.CustomerNotFound", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Appointment not found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Confirm_NonexistentAppointment_ReturnsNotFound()
    {
        Result<AppointmentDto> result = await _sut.ConfirmAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("Appointment.NotFound", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Reschedule
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Reschedule_Scheduled_CreatesNewAppointment()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Scheduled);
        DateTime newStart = DateTime.UtcNow.AddDays(1);
        DateTime newEnd = newStart.AddHours(1);

        Result<AppointmentDto> result = await _sut.RescheduleAsync(
            appointment.Id,
            new RescheduleAppointmentRequest(newStart, newEnd));

        Assert.True(result.IsSuccess);
        Assert.Equal("Scheduled", result.Value.Status);
        // The returned appointment is the NEW one, not the old one
        Assert.NotEqual(appointment.Id, result.Value.Id);
    }

    [Fact]
    public async Task Reschedule_Confirmed_CreatesNewAppointment()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Confirmed);
        DateTime newStart = DateTime.UtcNow.AddDays(1);
        DateTime newEnd = newStart.AddHours(1);

        Result<AppointmentDto> result = await _sut.RescheduleAsync(
            appointment.Id,
            new RescheduleAppointmentRequest(newStart, newEnd));

        Assert.True(result.IsSuccess);
        Assert.Equal("Scheduled", result.Value.Status);
    }

    [Fact]
    public async Task Reschedule_Cancelled_Fails()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Cancelled);

        Result<AppointmentDto> result = await _sut.RescheduleAsync(
            appointment.Id,
            new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(1)));

        Assert.True(result.IsFailure);
        Assert.Equal("Appointment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public async Task Reschedule_InvalidTimeRange_Fails()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Scheduled);

        Result<AppointmentDto> result = await _sut.RescheduleAsync(
            appointment.Id,
            new RescheduleAppointmentRequest(DateTime.UtcNow.AddHours(3), DateTime.UtcNow.AddHours(1)));

        Assert.True(result.IsFailure);
        Assert.Equal("Appointment.InvalidTimeRange", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Webhook idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task IngestFromWebhook_DuplicateExternalId_ReturnsExisting()
    {
        Customer customer = await SeedCustomerAsync();
        DateTime startsAt = DateTime.UtcNow.AddHours(1);
        DateTime endsAt = startsAt.AddHours(1);

        var webhook = new InboundAppointmentWebhook(
            ExternalId: "ext-123",
            CustomerId: customer.Id,
            StartsAt: startsAt,
            EndsAt: endsAt,
            ServiceName: "Haircut");

        Result<AppointmentDto> first = await _sut.IngestFromWebhookAsync(webhook);
        Result<AppointmentDto> second = await _sut.IngestFromWebhookAsync(webhook);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
    }

    // -----------------------------------------------------------------------
    // Events published
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Confirm_PublishesStatusChangedEvent()
    {
        Customer customer = await SeedCustomerAsync();
        Appointment appointment = await SeedAppointmentAsync(customer, AppointmentStatus.Scheduled);

        await _sut.ConfirmAsync(appointment.Id);

        await _mediator.Received(1).Publish(
            Arg.Is<AppointmentStatusChangedEvent>(e =>
                e.AppointmentId == appointment.Id &&
                e.OldStatus == AppointmentStatus.Scheduled &&
                e.NewStatus == AppointmentStatus.Confirmed),
            Arg.Any<CancellationToken>());
    }
}
