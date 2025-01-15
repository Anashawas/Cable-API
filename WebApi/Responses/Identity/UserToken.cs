namespace Cable.Responses.Identity;


/// <summary>
/// The user tokens
/// </summary>
/// <param name="AccessToken">The access token</param>
/// <param name="RefreshToken">The refresh token</param>
public record UserToken(string AccessToken, string RefreshToken);
