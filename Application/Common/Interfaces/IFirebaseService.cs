using Application.Common.Models.Results;
using Cable.Core.Enums;
using FirebaseAdmin.Messaging;

namespace Application.Common.Interfaces;

public interface IFirebaseService
{
    /// <summary>
    /// Validates Firebase ID token from the specified app
    /// </summary>
    /// <param name="idToken">Firebase ID token to validate</param>
    /// <param name="appType">Firebase app type (UserApp or StationApp)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Firebase token validation result with user details</returns>
    Task<FirebaseTokenValidationResult> ValidateFirebaseTokenAsync(
        string idToken,
        FirebaseAppType appType = FirebaseAppType.UserApp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets FirebaseMessaging instance for the specified app
    /// </summary>
    /// <param name="appType">Firebase app type (UserApp or StationApp)</param>
    /// <returns>FirebaseMessaging instance</returns>
    FirebaseMessaging GetFirebaseMessaging(FirebaseAppType appType = FirebaseAppType.UserApp);
}