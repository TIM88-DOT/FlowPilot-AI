using System.Reflection;
using FlowPilot.Application.Customers;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Customers;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests phone number normalization to E.164 format and uniqueness checks.
/// Uses CustomerService.CreateAsync to exercise NormalizeToE164 indirectly.
/// </summary>
public sealed class PhoneNormalizationTests : IDisposable
{
    private readonly TestDbFixture _fixture = new();
    private readonly CustomerService _sut;
    private readonly AppDbContext _db;

    public PhoneNormalizationTests()
    {
        _db = _fixture.CreateContext();
        _sut = new CustomerService(_db, _fixture.CurrentTenant);
    }

    public void Dispose()
    {
        _db.Dispose();
        _fixture.Dispose();
    }

    // -----------------------------------------------------------------------
    // Valid phone numbers
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("+14165551234")]     // E.164 Canada (already formatted)
    [InlineData("4165551234")]       // Local Canada format (Toronto area code)
    [InlineData("+33612345678")]     // France
    [InlineData("+14155551234")]     // US
    public async Task Create_ValidPhone_Succeeds(string phone)
    {
        var request = new CreateCustomerRequest(
            Phone: phone,
            FirstName: "Test");

        Result<CustomerDto> result = await _sut.CreateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("+", result.Value.Phone);
    }

    [Fact]
    public async Task Create_CanadianLocalNumber_NormalizesToE164()
    {
        var request = new CreateCustomerRequest(
            Phone: "4165551234",
            FirstName: "Test");

        Result<CustomerDto> result = await _sut.CreateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("+14165551234", result.Value.Phone);
    }

    // -----------------------------------------------------------------------
    // Invalid phone numbers
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("not-a-phone")]
    [InlineData("123")]
    [InlineData("")]
    [InlineData("abcdefghij")]
    public async Task Create_InvalidPhone_Fails(string phone)
    {
        var request = new CreateCustomerRequest(
            Phone: phone,
            FirstName: "Test");

        Result<CustomerDto> result = await _sut.CreateAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.InvalidPhone", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Uniqueness within tenant
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_DuplicatePhone_Fails()
    {
        await _sut.CreateAsync(new CreateCustomerRequest(Phone: "+14165550001", FirstName: "First"));

        Result<CustomerDto> result = await _sut.CreateAsync(
            new CreateCustomerRequest(Phone: "+14165550001", FirstName: "Second"));

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.PhoneTaken", result.Error.Code);
    }

    [Fact]
    public async Task Create_DuplicatePhone_DifferentFormat_Fails()
    {
        // Create with E.164 format
        await _sut.CreateAsync(new CreateCustomerRequest(Phone: "+14165550001", FirstName: "First"));

        // Try with local format — should normalize to same E.164 and fail
        Result<CustomerDto> result = await _sut.CreateAsync(
            new CreateCustomerRequest(Phone: "4165550001", FirstName: "Second"));

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.PhoneTaken", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Default consent on create
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Create_DefaultConsentStatus_IsPending()
    {
        var request = new CreateCustomerRequest(Phone: "+14165551111", FirstName: "New");

        Result<CustomerDto> result = await _sut.CreateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal("Pending", result.Value.ConsentStatus);
    }
}
