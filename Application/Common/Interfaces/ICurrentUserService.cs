namespace Application.Common.Interfaces;

public interface ICurrentUserService
{
    int? UserId { get; }
    string Token { get; }

    /// <summary>
    /// Which client app the current request's token was issued to
    /// (AuthApps.Consumer / AuthApps.Provider). Tokens issued before per-app
    /// sessions shipped carry no claim and resolve to consumer.
    /// </summary>
    string App { get; }
}