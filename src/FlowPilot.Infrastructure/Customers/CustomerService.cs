using FlowPilot.Application.Customers;
using FlowPilot.Domain.Entities;
using FlowPilot.Domain.Enums;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using PhoneNumbers;

namespace FlowPilot.Infrastructure.Customers;

/// <summary>
/// Implements customer CRUD, consent management, GDPR anonymization, and CSV import.
/// </summary>
public sealed class CustomerService : ICustomerService
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

    public CustomerService(AppDbContext db, ICurrentTenant currentTenant)
    {
        _db = db;
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        // Normalize phone to E.164
        string? normalizedPhone = NormalizeToE164(request.Phone);
        if (normalizedPhone is null)
            return Result.Failure<CustomerDto>(Error.Validation("Customer.InvalidPhone", "Phone number is not valid. Provide a number with country code (e.g. +14165551234)."));

        // Check uniqueness within tenant
        bool phoneExists = await _db.Customers
            .AnyAsync(c => c.Phone == normalizedPhone, cancellationToken);

        if (phoneExists)
            return Result.Failure<CustomerDto>(Error.Conflict("Customer.PhoneTaken", $"A customer with phone '{normalizedPhone}' already exists."));

        var customer = new Customer
        {
            Phone = normalizedPhone,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            PreferredLanguage = request.PreferredLanguage,
            Tags = request.Tags,
            ConsentStatus = ConsentStatus.Pending
        };

        _db.Customers.Add(customer);

        // Create initial consent record
        var consentRecord = new ConsentRecord
        {
            CustomerId = customer.Id,
            Status = ConsentStatus.Pending,
            Source = request.ConsentSource,
            Notes = request.ConsentNotes
        };

        _db.ConsentRecords.Add(consentRecord);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(customer));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CustomerDto>>> ListAsync(CustomerQuery query, CancellationToken cancellationToken = default)
    {
        IQueryable<Customer> q = _db.Customers.AsNoTracking();

        // Search by name, phone, or email
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim().ToLower();
            q = q.Where(c =>
                c.FirstName.ToLower().Contains(search) ||
                (c.LastName != null && c.LastName.ToLower().Contains(search)) ||
                c.Phone.Contains(search) ||
                (c.Email != null && c.Email.ToLower().Contains(search)));
        }

        // Filter by tag (comma-separated tags field)
        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            string tag = query.Tag.Trim().ToLower();
            q = q.Where(c => c.Tags != null && c.Tags.ToLower().Contains(tag));
        }

        // Filter by consent status
        if (query.ConsentStatus.HasValue)
            q = q.Where(c => c.ConsentStatus == query.ConsentStatus.Value);

        // Filter by minimum no-show score
        if (query.NoShowScoreGte.HasValue)
            q = q.Where(c => c.NoShowScore >= query.NoShowScoreGte.Value);

        int totalCount = await q.CountAsync(cancellationToken);

        List<CustomerDto> items = await q
            .OrderByDescending(c => c.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new CustomerDto(
                c.Id, c.Phone, c.Email, c.FirstName, c.LastName,
                c.PreferredLanguage, c.Tags, c.NoShowScore,
                c.ConsentStatus.ToString(), c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedResult<CustomerDto>(items, totalCount, query.Page, query.PageSize));
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Customer? customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
            return Result.Failure<CustomerDto>(Error.NotFound("Customer", id));

        return Result.Success(ToDto(customer));
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        Customer? customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
            return Result.Failure<CustomerDto>(Error.NotFound("Customer", id));

        // If phone is changing, normalize and check uniqueness
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            string? normalizedPhone = NormalizeToE164(request.Phone);
            if (normalizedPhone is null)
                return Result.Failure<CustomerDto>(Error.Validation("Customer.InvalidPhone", "Phone number is not valid."));

            if (normalizedPhone != customer.Phone)
            {
                bool phoneExists = await _db.Customers
                    .AnyAsync(c => c.Phone == normalizedPhone && c.Id != id, cancellationToken);

                if (phoneExists)
                    return Result.Failure<CustomerDto>(Error.Conflict("Customer.PhoneTaken", $"A customer with phone '{normalizedPhone}' already exists."));

                customer.Phone = normalizedPhone;
            }
        }

        if (request.FirstName is not null)
            customer.FirstName = request.FirstName;

        if (request.LastName is not null)
            customer.LastName = request.LastName;

        if (request.Email is not null)
            customer.Email = request.Email;

        if (request.PreferredLanguage is not null)
            customer.PreferredLanguage = request.PreferredLanguage;

        if (request.Tags is not null)
            customer.Tags = request.Tags;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(customer));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Customer? customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
            return Result.Failure(Error.NotFound("Customer", id));

        // GDPR anonymize: wipe PII fields
        customer.Phone = "ANONYMIZED";
        customer.Email = "anonymized@removed.invalid";
        customer.FirstName = "ANONYMIZED";
        customer.LastName = null;
        customer.Tags = null;

        // Soft delete
        customer.IsDeleted = true;
        customer.DeletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<List<ConsentRecordDto>>> GetConsentHistoryAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        bool customerExists = await _db.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == customerId, cancellationToken);

        if (!customerExists)
            return Result.Failure<List<ConsentRecordDto>>(Error.NotFound("Customer", customerId));

        List<ConsentRecordDto> records = await _db.ConsentRecords
            .AsNoTracking()
            .Where(cr => cr.CustomerId == customerId)
            .OrderByDescending(cr => cr.CreatedAt)
            .Select(cr => new ConsentRecordDto(
                cr.Id, cr.Status.ToString(), cr.Source.ToString(), cr.Notes, cr.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(records);
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> UpdateConsentAsync(Guid customerId, UpdateConsentRequest request, CancellationToken cancellationToken = default)
    {
        Customer? customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

        if (customer is null)
            return Result.Failure<CustomerDto>(Error.NotFound("Customer", customerId));

        // Update the customer's current consent status
        customer.ConsentStatus = request.Status;

        // Append a new consent record (append-only audit log)
        var consentRecord = new ConsentRecord
        {
            CustomerId = customerId,
            Status = request.Status,
            Source = request.Source,
            Notes = request.Notes
        };

        _db.ConsentRecords.Add(consentRecord);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(customer));
    }

    /// <inheritdoc />
    public async Task<Result<CsvImportResult>> ImportCsvAsync(Stream csvStream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(csvStream);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(headerLine))
            return Result.Failure<CsvImportResult>(Error.Validation("Customer.CsvEmpty", "CSV file is empty or missing a header row."));

        // Parse header to find column indexes
        string[] headers = headerLine.Split(',').Select(h => h.Trim().ToLower()).ToArray();
        int phoneIdx = Array.IndexOf(headers, "phone");
        int firstNameIdx = Array.IndexOf(headers, "firstname");
        int lastNameIdx = Array.IndexOf(headers, "lastname");
        int emailIdx = Array.IndexOf(headers, "email");
        int languageIdx = Array.IndexOf(headers, "language");
        int tagsIdx = Array.IndexOf(headers, "tags");

        if (phoneIdx < 0 || firstNameIdx < 0)
            return Result.Failure<CsvImportResult>(Error.Validation("Customer.CsvMissingColumns", "CSV must have at least 'phone' and 'firstname' columns."));

        // Load existing phones for this tenant to skip duplicates
        HashSet<string> existingPhones = (await _db.Customers
            .AsNoTracking()
            .Select(c => c.Phone)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var errors = new List<CsvRowError>();
        var customersToAdd = new List<Customer>();
        var consentRecordsToAdd = new List<ConsentRecord>();
        int row = 1;
        int skipped = 0;

        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            row++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] cols = line.Split(',').Select(c => c.Trim()).ToArray();

            if (cols.Length <= phoneIdx || cols.Length <= firstNameIdx)
            {
                errors.Add(new CsvRowError(row, "Row has fewer columns than expected."));
                continue;
            }

            string rawPhone = cols[phoneIdx];
            string firstName = cols[firstNameIdx];

            if (string.IsNullOrWhiteSpace(rawPhone) || string.IsNullOrWhiteSpace(firstName))
            {
                errors.Add(new CsvRowError(row, "Phone and first name are required."));
                continue;
            }

            string? normalizedPhone = NormalizeToE164(rawPhone);
            if (normalizedPhone is null)
            {
                errors.Add(new CsvRowError(row, $"Invalid phone number: '{rawPhone}'."));
                continue;
            }

            // Skip duplicates (existing in DB or already in this batch)
            if (existingPhones.Contains(normalizedPhone))
            {
                skipped++;
                continue;
            }

            existingPhones.Add(normalizedPhone);

            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                Id = customerId,
                Phone = normalizedPhone,
                FirstName = firstName,
                LastName = lastNameIdx >= 0 && cols.Length > lastNameIdx ? NullIfEmpty(cols[lastNameIdx]) : null,
                Email = emailIdx >= 0 && cols.Length > emailIdx ? NullIfEmpty(cols[emailIdx]) : null,
                PreferredLanguage = languageIdx >= 0 && cols.Length > languageIdx && !string.IsNullOrWhiteSpace(cols[languageIdx])
                    ? cols[languageIdx]
                    : "fr",
                Tags = tagsIdx >= 0 && cols.Length > tagsIdx ? NullIfEmpty(cols[tagsIdx]) : null,
                ConsentStatus = ConsentStatus.Pending
            };

            customersToAdd.Add(customer);

            consentRecordsToAdd.Add(new ConsentRecord
            {
                CustomerId = customerId,
                Status = ConsentStatus.Pending,
                Source = ConsentSource.Import,
                Notes = "Imported from CSV"
            });
        }

        if (customersToAdd.Count > 0)
        {
            _db.Customers.AddRange(customersToAdd);
            _db.ConsentRecords.AddRange(consentRecordsToAdd);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(new CsvImportResult(
            TotalRows: row - 1, // exclude header
            Imported: customersToAdd.Count,
            Skipped: skipped,
            Errors: errors));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Normalizes a phone string to E.164 format. Returns null if invalid.
    /// Tries parsing with default region "CA" (Canada) for local numbers.
    /// </summary>
    private static string? NormalizeToE164(string rawPhone)
    {
        try
        {
            PhoneNumber number = PhoneUtil.Parse(rawPhone, "CA");
            if (!PhoneUtil.IsValidNumber(number))
                return null;

            return PhoneUtil.Format(number, PhoneNumberFormat.E164);
        }
        catch (NumberParseException)
        {
            return null;
        }
    }

    private static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.Phone, c.Email, c.FirstName, c.LastName,
            c.PreferredLanguage, c.Tags, c.NoShowScore,
            c.ConsentStatus.ToString(), c.CreatedAt, c.UpdatedAt);

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
