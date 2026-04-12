using FlowPilot.Application.Customers;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Customers;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests GDPR anonymization on soft delete and consent management.
/// </summary>
public sealed class CustomerGdprTests : IDisposable
{
    private readonly TestDbFixture _fixture = new();
    private readonly CustomerService _sut;
    private readonly AppDbContext _db;

    public CustomerGdprTests()
    {
        _db = _fixture.CreateContext();
        _sut = new CustomerService(_db, _fixture.CurrentTenant);
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
            Phone = "+14165550001",
            Email = "test@example.com",
            FirstName = "John",
            LastName = "Doe",
            Tags = "vip,regular",
            ConsentStatus = ConsentStatus.OptedIn
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        return customer;
    }

    // -----------------------------------------------------------------------
    // GDPR anonymization on delete
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Delete_AnonymizesPiiFields()
    {
        Customer customer = await SeedCustomerAsync();

        Result result = await _sut.DeleteAsync(customer.Id);

        Assert.True(result.IsSuccess);

        // Must use IgnoreQueryFilters to see soft-deleted entities
        await using AppDbContext verifyDb = _fixture.CreateContext();
        Customer? deleted = await verifyDb.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == customer.Id);

        Assert.NotNull(deleted);
        Assert.Equal("ANONYMIZED", deleted.Phone);
        Assert.Equal("anonymized@removed.invalid", deleted.Email);
        Assert.Equal("ANONYMIZED", deleted.FirstName);
        Assert.Null(deleted.LastName);
        Assert.Null(deleted.Tags);
    }

    [Fact]
    public async Task Delete_SetsIsDeletedAndDeletedAt()
    {
        Customer customer = await SeedCustomerAsync();

        await _sut.DeleteAsync(customer.Id);

        await using AppDbContext verifyDb = _fixture.CreateContext();
        Customer? deleted = await verifyDb.Customers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == customer.Id);

        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
        Assert.NotNull(deleted.DeletedAt);
    }

    [Fact]
    public async Task Delete_CustomerNotFound_Fails()
    {
        Result result = await _sut.DeleteAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Contains("NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Delete_CustomerNotVisibleAfterSoftDelete()
    {
        Customer customer = await SeedCustomerAsync();

        await _sut.DeleteAsync(customer.Id);

        // Normal query (with global filters) should not find it
        Result<CustomerDto> getResult = await _sut.GetByIdAsync(customer.Id);
        Assert.True(getResult.IsFailure);
    }

    // -----------------------------------------------------------------------
    // Consent management
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateConsent_AppendsConsentRecord()
    {
        Customer customer = await SeedCustomerAsync();

        var request = new UpdateConsentRequest(
            Status: ConsentStatus.OptedOut,
            Source: ConsentSource.Manual,
            Notes: "Customer requested opt-out");

        Result<CustomerDto> result = await _sut.UpdateConsentAsync(customer.Id, request);

        Assert.True(result.IsSuccess);
        Assert.Equal("OptedOut", result.Value.ConsentStatus);

        // Verify consent record was appended
        Result<List<ConsentRecordDto>> history = await _sut.GetConsentHistoryAsync(customer.Id);
        Assert.True(history.IsSuccess);
        Assert.Contains(history.Value, r => r.Status == "OptedOut" && r.Source == "Manual");
    }

    [Fact]
    public async Task UpdateConsent_CustomerNotFound_Fails()
    {
        var request = new UpdateConsentRequest(ConsentStatus.OptedIn, ConsentSource.Api);

        Result<CustomerDto> result = await _sut.UpdateConsentAsync(Guid.NewGuid(), request);

        Assert.True(result.IsFailure);
    }

    // -----------------------------------------------------------------------
    // Update with partial fields
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Update_PartialFields_OnlyChangesProvided()
    {
        Customer customer = await SeedCustomerAsync();

        var request = new UpdateCustomerRequest(FirstName: "Jane");
        Result<CustomerDto> result = await _sut.UpdateAsync(customer.Id, request);

        Assert.True(result.IsSuccess);
        Assert.Equal("Jane", result.Value.FirstName);
        Assert.Equal("Doe", result.Value.LastName); // unchanged
        Assert.Equal("test@example.com", result.Value.Email); // unchanged
    }
}
