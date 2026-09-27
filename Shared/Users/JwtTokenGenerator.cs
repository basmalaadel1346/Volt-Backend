using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shared.Common.Abstractions;
using Shared.Users;

namespace Shared.Users;

public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtSettings _settings;
    private readonly IDateTimeProvider _dateTimeProvider;

    public JwtTokenGenerator(IOptions<JwtSettings> options, IDateTimeProvider dateTimeProvider)
    {
        _settings = options.Value;
        _dateTimeProvider = dateTimeProvider;
    }

    public (string Token, DateTime ExpiresAt) GenerateAccessToken(Guid userId, string role, string authProvider, bool isEmailVerified)
    {
        var expiresAt = _dateTimeProvider.UtcNow.AddMinutes(_settings.AccessTokenExpirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("authProvider", authProvider),
            // Claim منفصل عن الـ Role/authProvider - بيستخدمه VerifiedEmailAuthorizationHandler
            // عشان يقرر يمنع Email users غير الموثقين من الـ Endpoints المحمية من غير ما
            // يحتاج يعمل Query على الداتابيز في كل Request
            new Claim("emailVerified", isEmailVerified ? "true" : "false"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public string GenerateRefreshTokenValue() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
