namespace FlowPilot.Application.Auth;

/// <summary>
/// Request to register a new tenant with an owner account.
/// </summary>
public sealed record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string BusinessName,
    string? BusinessPhone = null,
    string? Timezone = null,
    string DefaultLanguage = "fr");

/// <summary>
/// Request to log in with email and password.
/// </summary>
public sealed record LoginRequest(
    string Email,
    string Password);

/// <summary>
/// Response containing the access token. Refresh token is set in httpOnly cookie.
/// </summary>
public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    UserInfo User)
{
    /// <summary>
    /// Raw refresh token — only used server-side to set httpOnly cookie.
    /// Excluded from JSON serialization via System.Text.Json ignore.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string RawRefreshToken { get; init; } = string.Empty;
}

/// <summary>
/// Basic user information returned after auth operations.
/// </summary>
public sealed record UserInfo(
    Guid Id,
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string BusinessName);
