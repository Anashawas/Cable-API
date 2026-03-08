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
    Task<string> SendOtpAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<UserLoginResult> LoginWithOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken);

    // Provider 2FA Authentication (Email/Password + OTP)
    Task<ProviderAuthSessionResult> LoginProvider(string email, string password, CancellationToken cancellationToken = default);
    Task<string> SendProviderOtpAsync(string sessionToken, CancellationToken cancellationToken = default);
    Task<UserLoginResult> VerifyProviderOtpAsync(string sessionToken, string otp, CancellationToken cancellationToken = default);

    // Session management
    Task Logout(int userId, CancellationToken cancellationToken = default);
}
