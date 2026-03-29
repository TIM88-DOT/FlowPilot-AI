using System.Text;
using FlowPilot.Application.Customers;
using FlowPilot.Infrastructure.Customers;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;

namespace FlowPilot.UnitTests;

/// <summary>
/// Tests CSV customer import: parsing, validation, deduplication, and error handling.
/// </summary>
public sealed class CsvImportTests : IDisposable
{
    private readonly TestDbFixture _fixture = new();
    private readonly CustomerService _sut;
    private readonly AppDbContext _db;

    public CsvImportTests()
    {
        _db = _fixture.CreateContext();
        _sut = new CustomerService(_db, _fixture.CurrentTenant);
    }

    public void Dispose()
    {
        _db.Dispose();
        _fixture.Dispose();
    }

    private static MemoryStream ToCsvStream(string csv) =>
        new(Encoding.UTF8.GetBytes(csv));

    [Fact]
    public async Task Import_ValidCsv_ImportsAll()
    {
        string csv = """
            phone,firstname,lastname,email,language,tags
            +213555000001,Ali,Benaissa,ali@test.com,fr,vip
            +213555000002,Sara,Mansouri,sara@test.com,ar,
            """;

        Result<CsvImportResult> result = await _sut.ImportCsvAsync(ToCsvStream(csv));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalRows);
        Assert.Equal(2, result.Value.Imported);
        Assert.Equal(0, result.Value.Skipped);
        Assert.Empty(result.Value.Errors);
    }

    [Fact]
    public async Task Import_EmptyCsv_Fails()
    {
        Result<CsvImportResult> result = await _sut.ImportCsvAsync(ToCsvStream(""));

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.CsvEmpty", result.Error.Code);
    }

    [Fact]
    public async Task Import_MissingRequiredColumns_Fails()
    {
        string csv = "email,tags\ntest@test.com,vip";

        Result<CsvImportResult> result = await _sut.ImportCsvAsync(ToCsvStream(csv));

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.CsvMissingColumns", result.Error.Code);
    }

    [Fact]
    public async Task Import_InvalidPhone_ReportsRowError()
    {
        string csv = """
            phone,firstname
            not-a-phone,Ali
            +213555000003,Sara
            """;

        Result<CsvImportResult> result = await _sut.ImportCsvAsync(ToCsvStream(csv));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Imported);
        Assert.Single(result.Value.Errors);
        Assert.Equal(2, result.Value.Errors[0].Row); // row 2 (after header)
    }

    [Fact]
    public async Task Import_DuplicatePhoneInCsv_SkipsSecond()
    {
        string csv = """
            phone,firstname
            +213555000001,Ali
            +213555000001,Sara
            """;

        Result<CsvImportResult> result = await _sut.ImportCsvAsync(ToCsvStream(csv));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Imported);
        Assert.Equal(1, result.Value.Skipped);
    }

    [Fact]
    public async Task Import_PhoneExistsInDb_SkipsIt()
    {
        // Pre-seed a customer
        await _sut.CreateAsync(new CreateCustomerRequest(Phone: "+213555000001", FirstName: "Existing"));

        string csv = """
            phone,firstname
            +213555000001,Ali
            +213555000002,Sara
            """;

        Result<CsvImportResult> result = await _sut.ImportCsvAsync(ToCsvStream(csv));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Imported);
        Assert.Equal(1, result.Value.Skipped);
    }

    [Fact]
    public async Task Import_MissingPhone_ReportsError()
    {
        string csv = """
            phone,firstname
            ,Ali
            """;

        Result<CsvImportResult> result = await _sut.ImportCsvAsync(ToCsvStream(csv));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Imported);
        Assert.Single(result.Value.Errors);
    }

    [Fact]
    public async Task Import_DefaultLanguageIsFr()
    {
        string csv = """
            phone,firstname,language
            +213555000001,Ali,
            """;

        Result<CsvImportResult> result = await _sut.ImportCsvAsync(ToCsvStream(csv));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Imported);

        // Verify the customer's language is "fr"
        await using AppDbContext verifyDb = _fixture.CreateContext();
        var customer = verifyDb.Customers.First();
        Assert.Equal("fr", customer.PreferredLanguage);
    }

    [Fact]
    public async Task Import_ConsentStatusIsPending()
    {
        string csv = """
            phone,firstname
            +213555000001,Ali
            """;

        await _sut.ImportCsvAsync(ToCsvStream(csv));

        await using AppDbContext verifyDb = _fixture.CreateContext();
        var customer = verifyDb.Customers.First();
        Assert.Equal(Domain.Enums.ConsentStatus.Pending, customer.ConsentStatus);
    }
}
