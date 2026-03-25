using FlowPilot.Shared;

namespace FlowPilot.Application.Customers;

/// <summary>
/// Customer CRUD, consent management, GDPR anonymization, and CSV import.
/// </summary>
public interface ICustomerService
{
    /// <summary>
    /// Creates a new customer with an initial consent record.
    /// Phone is normalized to E.164 format.
    /// </summary>
    Task<Result<CustomerDto>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paginated, filtered list of customers for the current tenant.
    /// </summary>
    Task<Result<PagedResult<CustomerDto>>> ListAsync(CustomerQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single customer by id.
    /// </summary>
    Task<Result<CustomerDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates mutable customer fields. Phone is re-normalized to E.164 if changed.
    /// </summary>
    Task<Result<CustomerDto>> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// GDPR anonymize: wipes PII fields and soft-deletes the customer.
    /// </summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the consent history (append-only log) for a customer.
    /// </summary>
    Task<Result<List<ConsentRecordDto>>> GetConsentHistoryAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates consent status and appends a new consent record.
    /// </summary>
    Task<Result<CustomerDto>> UpdateConsentAsync(Guid customerId, UpdateConsentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports customers from a CSV stream. Normalizes phones to E.164, sets consent to Pending.
    /// </summary>
    Task<Result<CsvImportResult>> ImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default);
}
