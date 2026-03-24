using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FlowPilot.Application.Auth;
using FlowPilot.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlowPilot.Infrastructure.Auth;

/// <summary>
/// Generates JWT access tokens and opaque refresh tokens.
/// </summary>
public sealed class JwtTokenService
{
    private readonly JwtSettings _settings;

    public JwtTokenService(IOptions<JwtSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>
    /// Creates a signed JWT access token with tenant_id, sub, role, and email claims.
    /// </summary>
    public (string Token, DateTime ExpiresAt) GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        DateTime expiresAt = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes);

        var claims = new[]
        {
            new Claim("sub", user.Id.ToString()),
            new Claim("tenant_id", user.TenantId.ToString()),
            new Claim("role", user.Role.ToString()),
            new Claim("email", user.Email),
            new Claim("given_name", user.FirstName),
            new Claim("family_name", user.LastName)
        };

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>
    /// Generates a cryptographically random refresh token and its expiration.
    /// </summary>
    public (string Token, DateTime ExpiresAt) GenerateRefreshToken()
    {
        byte[] randomBytes = RandomNumberGenerator.GetBytes(64);
        string token = Convert.ToBase64String(randomBytes);
        DateTime expiresAt = DateTime.UtcNow.AddDays(_settings.RefreshTokenExpirationDays);
        return (token, expiresAt);
    }
}
