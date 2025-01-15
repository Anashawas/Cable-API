using Application.Common.Models;

namespace Application.Common.Interfaces;

public interface IAuthenticationService
{
    Task<UserLoginByTokenResult> LoginByToken(string token, CancellationToken cancellationToken = default);
    Task<UserLoginResult> Login(int id, CancellationToken cancellationToken = default);
    Task<UserLoginResult> Login(string username, string password, CancellationToken cancellationToken = default);
    Task<(string accessToken, string refreshToken)> RefreshTokens(string refreshToken);

}
