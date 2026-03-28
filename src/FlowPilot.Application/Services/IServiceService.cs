using FlowPilot.Shared;

namespace FlowPilot.Application.Services;

/// <summary>
/// Manages tenant services (e.g. Haircut, Consultation) used in appointment creation.
/// </summary>
public interface IServiceService
{
    /// <summary>
    /// Returns all services for the current tenant, ordered by SortOrder then Name.
    /// </summary>
    Task<Result<List<ServiceDto>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single service by id.
    /// </summary>
    Task<Result<ServiceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new service for the current tenant.
    /// </summary>
    Task<Result<ServiceDto>> CreateAsync(CreateServiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates mutable service fields.
    /// </summary>
    Task<Result<ServiceDto>> UpdateAsync(Guid id, UpdateServiceRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a service.
    /// </summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
