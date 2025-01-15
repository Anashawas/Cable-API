namespace Infrastructrue.Options;

public class TokenOptions
{
    public const string ConfigName = "Token";

    public TimeSpan AccessTokenExpiresAfter { get; set; }
    public TimeSpan RefreshTokenExpiresAfter { get; set; }
}