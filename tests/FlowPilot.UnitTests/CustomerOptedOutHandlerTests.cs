using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Messaging;
using FlowPilot.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests that the CustomerOptedOutHandler cancels all pending scheduled messages.
/// </summary>
public sealed class CustomerOptedOutHandlerTests : IDisposable
{
    private readonly TestDbFixture _fixture = new();
    private readonly CustomerOptedOutHandler _sut;
    private readonly AppDbContext _db;

    public CustomerOptedOutHandlerTests()
    {
        _db = _fixture.CreateContext();
        _sut = new CustomerOptedOutHandler(_db, Substitute.For<ILogger<CustomerOptedOutHandler>>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _fixture.Dispose();
    }

    private async Task<(Customer customer, Appointment appointment)> SeedDataAsync()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            Phone = "+14165550001",
            FirstName = "Test",
            ConsentStatus = ConsentStatus.OptedIn
        };
        _db.Customers.Add(customer);

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            CustomerId = customer.Id,
            Status = AppointmentStatus.Scheduled,
            StartsAt = DateTime.UtcNow.AddHours(2),
            EndsAt = DateTime.UtcNow.AddHours(3)
        };
        _db.Appointments.Add(appointment);

        await _db.SaveChangesAsync();
        return (customer, appointment);
    }

    [Fact]
    public async Task Handle_CancelsAllPendingScheduledMessages()
    {
        (Customer customer, Appointment appointment) = await SeedDataAsync();

        // Seed 3 pending messages
        for (int i = 0; i < 3; i++)
        {
            _db.ScheduledMessages.Add(new ScheduledMessage
            {
                Id = Guid.NewGuid(),
                TenantId = _fixture.TenantId,
                CustomerId = customer.Id,
                AppointmentId = appointment.Id,
                Status = ScheduledMessageStatus.Pending,
                ScheduledAt = DateTime.UtcNow.AddHours(1)
            });
        }
        await _db.SaveChangesAsync();

        await _sut.Handle(new CustomerOptedOutEvent(customer.Id, _fixture.TenantId), CancellationToken.None);

        await using AppDbContext verifyDb = _fixture.CreateContext();
        List<ScheduledMessage> messages = verifyDb.ScheduledMessages
            .Where(sm => sm.CustomerId == customer.Id)
            .ToList();

        Assert.All(messages, m => Assert.Equal(ScheduledMessageStatus.Cancelled, m.Status));
    }

    [Fact]
    public async Task Handle_DoesNotCancelAlreadySentMessages()
    {
        (Customer customer, Appointment appointment) = await SeedDataAsync();

        _db.ScheduledMessages.Add(new ScheduledMessage
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            CustomerId = customer.Id,
            AppointmentId = appointment.Id,
            Status = ScheduledMessageStatus.Sent,
            ScheduledAt = DateTime.UtcNow.AddHours(-1),
            SentAt = DateTime.UtcNow
        });

        _db.ScheduledMessages.Add(new ScheduledMessage
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            CustomerId = customer.Id,
            AppointmentId = appointment.Id,
            Status = ScheduledMessageStatus.Pending,
            ScheduledAt = DateTime.UtcNow.AddHours(1)
        });
        await _db.SaveChangesAsync();

        await _sut.Handle(new CustomerOptedOutEvent(customer.Id, _fixture.TenantId), CancellationToken.None);

        await using AppDbContext verifyDb = _fixture.CreateContext();
        List<ScheduledMessage> messages = verifyDb.ScheduledMessages
            .Where(sm => sm.CustomerId == customer.Id)
            .OrderBy(sm => sm.ScheduledAt)
            .ToList();

        Assert.Equal(2, messages.Count);
        Assert.Equal(ScheduledMessageStatus.Sent, messages[0].Status);       // Sent stays Sent
        Assert.Equal(ScheduledMessageStatus.Cancelled, messages[1].Status);  // Pending gets cancelled
    }

    [Fact]
    public async Task Handle_NoPendingMessages_CompletesWithoutError()
    {
        (Customer customer, _) = await SeedDataAsync();

        // No scheduled messages at all
        await _sut.Handle(new CustomerOptedOutEvent(customer.Id, _fixture.TenantId), CancellationToken.None);

        // Should complete without exception
    }
}
