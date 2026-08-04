using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cable.Security.Jwt.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cable.Security.Jwt;

public class TokenGenerationService : ITokenGenerationService
{
    private readonly TokenGeneationOptions _tokenGenerationOptions;

    public TokenGenerationService(IOptions<TokenGeneationOptions> tokenValidationParameters)
    {
        if (tokenValidationParameters == null)
        {
            throw new ArgumentNullException(nameof(tokenValidationParameters));
        }

        if (tokenValidationParameters.Value == null)
        {
            throw new ArgumentNullException(nameof(tokenValidationParameters.Value));
        }

        _tokenGenerationOptions = tokenValidationParameters.Value;
    }

    public string GenerateToken(Claim[] claims) => GenerateToken(claims, _tokenGenerationOptions.ExpiresAfter).Token;

    public (string Token, DateTime? ExpiresAt) GenerateToken(Claim[] claims, TimeSpan? expireAfter)
    {
        var jwtToken = new JwtSecurityToken(
            _tokenGenerationOptions.TokenValidationParameters.ValidIssuer,
            _tokenGenerationOptions.TokenValidationParameters.ValidAudience,
            claims,
            expires: expireAfter.HasValue
                ? DateTime.UtcNow.AddTicks(expireAfter.Value.Ticks)
                : (DateTime?)null,
            signingCredentials: new SigningCredentials(
                _tokenGenerationOptions.TokenValidationParameters.IssuerSigningKey,
                SecurityAlgorithms.HmacSha256Signature));

        var tokenString = new JwtSecurityTokenHandler().WriteToken(jwtToken);

        return (tokenString, jwtToken.ValidTo);
    }

    public (ClaimsPrincipal claimsPrincipal, JwtSecurityToken securityToken) DecodeToken(string token)
    {
        var principal = new JwtSecurityTokenHandler().ValidateToken(token,
            _tokenGenerationOptions.TokenValidationParameters, out var validatedToken);

        return (principal, validatedToken as JwtSecurityToken);
    }
}