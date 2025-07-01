using Application.Common.Models;
using Google.Apis.Auth;

namespace Application.Common.Interfaces;

public interface IAuthenticationService
{
    Task<UserLoginByTokenResult> LoginByToken(string token, CancellationToken cancellationToken = default);
    Task<UserLoginResult> Login(int id, CancellationToken cancellationToken = default);
    Task<UserLoginResult> Login(string email, string password, CancellationToken cancellationToken = default);
    Task<(string accessToken, string refreshToken)> RefreshTokens(string refreshToken);
    Task<UserLoginResult> LoginFirebaseAsync(FirebaseLoginDetails firebaseLoginDetails ,CancellationToken cancellationToken);
}
