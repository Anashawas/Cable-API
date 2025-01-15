using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Cable.Security.Jwt.Interfaces;

public interface ITokenGenerationService
{
    /// <summary>
    /// Generates a token without an expiry
    /// </summary>
    /// <param name="claims">The claims to include in the token</param>
    /// <returns>A claims token</returns>
    public string GenerateToken(Claim[] claims);

    /// <summary>
    /// Generates a token.
    /// </summary>
    /// <param name="claims">The claims to include in the token</param>
    /// <param name="expireAfter">The time after which the token will expire</param>
    /// <returns>A claims token</returns>
    public (String Token, DateTime? ExpiresAt) GenerateToken(Claim[] claims, TimeSpan? expireAfter);

    /// <summary>
    /// Decodes a token
    /// </summary>
    /// <param name="token"></param>
    /// <returns>The claims and Security Token</returns>
    public (ClaimsPrincipal claimsPrincipal, JwtSecurityToken securityToken) DecodeToken(string token);
}
