namespace FlowPilot.Application.Services;

/// <summary>
/// DTO returned for a tenant service.
/// </summary>
public sealed record ServiceDto(
    Guid Id,
    string Name,
    int DurationMinutes,
    decimal? Price,
    string? Currency,
    bool IsActive,
    int SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Request to create a new service.
/// </summary>
public sealed record CreateServiceRequest(
    string Name,
    int DurationMinutes = 30,
    decimal? Price = null,
    string? Currency = null,
    bool IsActive = true);

/// <summary>
/// Request to update an existing service.
/// </summary>
public sealed record UpdateServiceRequest(
    string? Name = null,
    int? DurationMinutes = null,
    decimal? Price = null,
    string? Currency = null,
    bool? IsActive = null,
    int? SortOrder = null);
