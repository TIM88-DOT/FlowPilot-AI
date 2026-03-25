using FlowPilot.Domain.Enums;

namespace FlowPilot.Application.Customers;

/// <summary>
/// Request to create a new customer with an initial consent source.
/// </summary>
public sealed record CreateCustomerRequest(
    string Phone,
    string FirstName,
    string? LastName = null,
    string? Email = null,
    string PreferredLanguage = "fr",
    string? Tags = null,
    ConsentSource ConsentSource = ConsentSource.Manual,
    string? ConsentNotes = null);

/// <summary>
/// Request to update an existing customer's mutable fields.
/// </summary>
public sealed record UpdateCustomerRequest(
    string? Phone = null,
    string? FirstName = null,
    string? LastName = null,
    string? Email = null,
    string? PreferredLanguage = null,
    string? Tags = null);

/// <summary>
/// Request to change a customer's consent status.
/// </summary>
public sealed record UpdateConsentRequest(
    ConsentStatus Status,
    ConsentSource Source,
    string? Notes = null);

/// <summary>
/// Paginated query parameters for listing customers.
/// </summary>
public sealed record CustomerQuery(
    string? Search = null,
    string? Tag = null,
    ConsentStatus? ConsentStatus = null,
    decimal? NoShowScoreGte = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>
/// Customer data returned to callers.
/// </summary>
public sealed record CustomerDto(
    Guid Id,
    string Phone,
    string? Email,
    string FirstName,
    string? LastName,
    string PreferredLanguage,
    string? Tags,
    decimal NoShowScore,
    string ConsentStatus,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Consent record data returned in history.
/// </summary>
public sealed record ConsentRecordDto(
    Guid Id,
    string Status,
    string Source,
    string? Notes,
    DateTime CreatedAt);

/// <summary>
/// Paginated result wrapper.
/// </summary>
public sealed record PagedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>
/// A single row from a CSV import with its processing result.
/// </summary>
public sealed record CsvImportResult(
    int TotalRows,
    int Imported,
    int Skipped,
    List<CsvRowError> Errors);

/// <summary>
/// Describes an error for a specific CSV row.
/// </summary>
public sealed record CsvRowError(
    int Row,
    string Error);
