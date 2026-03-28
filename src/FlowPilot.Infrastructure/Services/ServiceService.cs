using FlowPilot.Application.Services;
using FlowPilot.Domain.Entities;
using FlowPilot.Infrastructure.Persistence;
using FlowPilot.Shared;
using FlowPilot.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FlowPilot.Infrastructure.Services;

/// <summary>
/// Manages tenant services — CRUD with soft delete, tenant-isolated via global query filters.
/// </summary>
public sealed class ServiceService : IServiceService
{
    private readonly AppDbContext _db;

    public ServiceService(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<Result<List<ServiceDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        List<ServiceDto> items = await _db.Services
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Select(s => ToDto(s))
            .ToListAsync(cancellationToken);

        return Result.Success(items);
    }

    /// <inheritdoc />
    public async Task<Result<ServiceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Service? service = await _db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (service is null)
            return Result.Failure<ServiceDto>(Error.NotFound("Service", id));

        return Result.Success(ToDto(service));
    }

    /// <inheritdoc />
    public async Task<Result<ServiceDto>> CreateAsync(CreateServiceRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result.Failure<ServiceDto>(Error.Validation("Service.InvalidName", "Service name is required."));

        bool nameTaken = await _db.Services
            .AnyAsync(s => s.Name.ToLower() == request.Name.Trim().ToLower(), cancellationToken);

        if (nameTaken)
            return Result.Failure<ServiceDto>(Error.Conflict("Service.NameTaken", $"A service named '{request.Name}' already exists."));

        int maxSortOrder = await _db.Services
            .OrderByDescending(s => s.SortOrder)
            .Select(s => s.SortOrder)
            .FirstOrDefaultAsync(cancellationToken);

        var service = new Service
        {
            Name = request.Name.Trim(),
            DurationMinutes = request.DurationMinutes,
            Price = request.Price,
            Currency = request.Currency,
            IsActive = request.IsActive,
            SortOrder = maxSortOrder + 1
        };

        _db.Services.Add(service);
        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(service));
    }

    /// <inheritdoc />
    public async Task<Result<ServiceDto>> UpdateAsync(Guid id, UpdateServiceRequest request, CancellationToken cancellationToken = default)
    {
        Service? service = await _db.Services
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (service is null)
            return Result.Failure<ServiceDto>(Error.NotFound("Service", id));

        if (request.Name is not null)
        {
            string trimmedName = request.Name.Trim();
            bool nameTaken = await _db.Services
                .AnyAsync(s => s.Id != id && s.Name.ToLower() == trimmedName.ToLower(), cancellationToken);

            if (nameTaken)
                return Result.Failure<ServiceDto>(Error.Conflict("Service.NameTaken", $"A service named '{trimmedName}' already exists."));

            service.Name = trimmedName;
        }

        if (request.DurationMinutes.HasValue)
            service.DurationMinutes = request.DurationMinutes.Value;

        if (request.Price.HasValue)
            service.Price = request.Price.Value;

        if (request.Currency is not null)
            service.Currency = request.Currency;

        if (request.IsActive.HasValue)
            service.IsActive = request.IsActive.Value;

        if (request.SortOrder.HasValue)
            service.SortOrder = request.SortOrder.Value;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(service));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Service? service = await _db.Services
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (service is null)
            return Result.Failure(Error.NotFound("Service", id));

        service.IsDeleted = true;
        service.DeletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static ServiceDto ToDto(Service s) =>
        new(s.Id, s.Name, s.DurationMinutes, s.Price, s.Currency,
            s.IsActive, s.SortOrder, s.CreatedAt, s.UpdatedAt);
}
