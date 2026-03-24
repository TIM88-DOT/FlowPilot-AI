using FlowPilot.Domain.Common;
using FlowPilot.Domain.Enums;

namespace FlowPilot.Domain.Entities;

/// <summary>
/// Application user belonging to a tenant. Roles: Owner, Manager, Staff.
/// </summary>
public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
}
