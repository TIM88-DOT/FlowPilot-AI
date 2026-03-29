using FlowPilot.Application.Messaging;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Messaging;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests the SMS consent gate, STOP keyword opt-out, and inbound message idempotency.
/// </summary>
public sealed class ConsentGateTests : IDisposable
{
    private readonly TestDbFixture _fixture = new();
    private readonly ISmsProvider _smsProvider = Substitute.For<ISmsProvider>();
    private readonly ITemplateRenderer _templateRenderer = Substitute.For<ITemplateRenderer>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly MessagingService _sut;
    private readonly AppDbContext _db;

    public ConsentGateTests()
    {
        _db = _fixture.CreateContext();
        _sut = new MessagingService(
            _db,
            _smsProvider,
            _templateRenderer,
            _fixture.CurrentTenant,
            _mediator,
            Substitute.For<ILogger<MessagingService>>());
    }

    public void Dispose()
    {
        _db.Dispose();
        _fixture.Dispose();
    }

    private async Task<Customer> SeedCustomerAsync(ConsentStatus consent = ConsentStatus.OptedIn, string phone = "+213555000001")
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _fixture.TenantId,
            Phone = phone,
            FirstName = "Test",
            ConsentStatus = consent,
            PreferredLanguage = "fr"
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    // -----------------------------------------------------------------------
    // Consent gate blocks non-OptedIn customers
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(ConsentStatus.Pending)]
    [InlineData(ConsentStatus.OptedOut)]
    public async Task SendRaw_NonOptedIn_Blocked(ConsentStatus status)
    {
        Customer customer = await SeedCustomerAsync(consent: status);
        var request = new SendRawSmsRequest(customer.Id, "Hello!");

        Result<SendSmsResponse> result = await _sut.SendRawAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Messaging.ConsentRequired", result.Error.Code);
        Assert.Contains(status.ToString(), result.Error.Description);
    }

    [Fact]
    public async Task SendRaw_OptedIn_Allowed()
    {
        Customer customer = await SeedCustomerAsync(consent: ConsentStatus.OptedIn);

        // Seed tenant so TenantSettings FK is satisfied
        _db.Tenants.Add(new Tenant
        {
            Id = _fixture.TenantId,
            TenantId = _fixture.TenantId,
            BusinessName = "Test Clinic",
            Settings = new TenantSettings
            {
                TenantId = _fixture.TenantId,
                OwnerTenantId = _fixture.TenantId,
                DefaultSenderPhone = "+10000000000"
            }
        });
        await _db.SaveChangesAsync();

        _smsProvider.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SmsResult(true, "MSG123", 1));

        var request = new SendRawSmsRequest(customer.Id, "Hello!");

        Result<SendSmsResponse> result = await _sut.SendRawAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("MSG123", result.Value.ProviderMessageId);
    }

    [Fact]
    public async Task SendRaw_CustomerNotFound_Fails()
    {
        var request = new SendRawSmsRequest(Guid.NewGuid(), "Hello!");

        Result<SendSmsResponse> result = await _sut.SendRawAsync(request);

        Assert.True(result.IsFailure);
        Assert.Contains("NotFound", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // STOP keyword opt-out
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("STOP")]
    [InlineData("stop")]
    [InlineData("Unsubscribe")]
    [InlineData("CANCEL")]
    [InlineData("END")]
    [InlineData("QUIT")]
    [InlineData("ARRET")]
    [InlineData("ARRETER")]
    public async Task ProcessInbound_StopKeyword_OptsOut(string keyword)
    {
        Customer customer = await SeedCustomerAsync(consent: ConsentStatus.OptedIn);

        var webhook = new InboundSmsWebhook(
            ProviderSmsSid: $"SM_{keyword}_{Guid.NewGuid()}",
            FromPhone: customer.Phone,
            ToPhone: "+10000000000",
            Body: keyword);

        Result result = await _sut.ProcessInboundAsync(webhook);

        Assert.True(result.IsSuccess);

        // Verify customer was opted out
        await using AppDbContext verifyDb = _fixture.CreateContext();
        Customer? updated = await verifyDb.Customers.FindAsync(customer.Id);
        Assert.Equal(ConsentStatus.OptedOut, updated!.ConsentStatus);
    }

    [Fact]
    public async Task ProcessInbound_StopKeyword_CreatesConsentRecord()
    {
        Customer customer = await SeedCustomerAsync(consent: ConsentStatus.OptedIn);

        var webhook = new InboundSmsWebhook(
            ProviderSmsSid: $"SM_{Guid.NewGuid()}",
            FromPhone: customer.Phone,
            ToPhone: "+10000000000",
            Body: "STOP");

        await _sut.ProcessInboundAsync(webhook);

        await using AppDbContext verifyDb = _fixture.CreateContext();
        ConsentRecord? record = verifyDb.ConsentRecords
            .Where(cr => cr.CustomerId == customer.Id && cr.Source == ConsentSource.SmsOptOut)
            .OrderByDescending(cr => cr.CreatedAt)
            .FirstOrDefault();

        Assert.NotNull(record);
        Assert.Equal(ConsentStatus.OptedOut, record.Status);
    }

    [Fact]
    public async Task ProcessInbound_StopKeyword_PublishesOptedOutEvent()
    {
        Customer customer = await SeedCustomerAsync(consent: ConsentStatus.OptedIn);

        var webhook = new InboundSmsWebhook(
            ProviderSmsSid: $"SM_{Guid.NewGuid()}",
            FromPhone: customer.Phone,
            ToPhone: "+10000000000",
            Body: "STOP");

        await _sut.ProcessInboundAsync(webhook);

        await _mediator.Received(1).Publish(
            Arg.Is<CustomerOptedOutEvent>(e => e.CustomerId == customer.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessInbound_NormalMessage_DoesNotOptOut()
    {
        Customer customer = await SeedCustomerAsync(consent: ConsentStatus.OptedIn);

        var webhook = new InboundSmsWebhook(
            ProviderSmsSid: $"SM_{Guid.NewGuid()}",
            FromPhone: customer.Phone,
            ToPhone: "+10000000000",
            Body: "Hello, I want to confirm my appointment");

        Result result = await _sut.ProcessInboundAsync(webhook);

        Assert.True(result.IsSuccess);

        await using AppDbContext verifyDb = _fixture.CreateContext();
        Customer? updated = await verifyDb.Customers.FindAsync(customer.Id);
        Assert.Equal(ConsentStatus.OptedIn, updated!.ConsentStatus);
    }

    // -----------------------------------------------------------------------
    // Inbound SMS idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProcessInbound_DuplicateSmsSid_SkipsProcessing()
    {
        Customer customer = await SeedCustomerAsync(consent: ConsentStatus.OptedIn);
        string smsSid = $"SM_{Guid.NewGuid()}";

        var webhook = new InboundSmsWebhook(
            ProviderSmsSid: smsSid,
            FromPhone: customer.Phone,
            ToPhone: "+10000000000",
            Body: "Hello");

        await _sut.ProcessInboundAsync(webhook);
        Result second = await _sut.ProcessInboundAsync(webhook);

        Assert.True(second.IsSuccess);

        // Should only have one message logged
        await using AppDbContext verifyDb = _fixture.CreateContext();
        int messageCount = verifyDb.Messages.Count(m => m.ProviderSmsSid == smsSid);
        Assert.Equal(1, messageCount);
    }

    [Fact]
    public async Task ProcessInbound_UnknownPhone_ReturnsSuccessGracefully()
    {
        var webhook = new InboundSmsWebhook(
            ProviderSmsSid: $"SM_{Guid.NewGuid()}",
            FromPhone: "+213555999999",
            ToPhone: "+10000000000",
            Body: "Who is this?");

        Result result = await _sut.ProcessInboundAsync(webhook);

        Assert.True(result.IsSuccess);
    }
}
