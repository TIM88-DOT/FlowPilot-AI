using FlowPilot.Shared;

namespace FlowPilot.Application.Auth;

/// <summary>
/// Handles authentication operations: register, login, refresh, and logout.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Registers a new tenant with an Owner user, default Plan, default TenantSettings,
    /// and seeds system templates with fr + ar locale variants.
    /// </summary>
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a user by email and password. Returns access token in body;
    /// refresh token must be set as httpOnly cookie by the caller.
    /// </summary>
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates the refresh token and returns a new access token.
    /// </summary>
    Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the refresh token for the given user.
    /// </summary>
    Task<Result> LogoutAsync(Guid userId, CancellationToken cancellationToken = default);
}
