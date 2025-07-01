using Application.Common.Models.Results;
using FirebaseAdmin.Messaging;

namespace Application.Common.Interfaces;

public interface IFirebaseService
{
    Task<FirebaseTokenValidationResult> ValidateFirebaseTokenAsync(string idToken, CancellationToken cancellationToken = default);
   FirebaseMessaging FirebaseMessaging {  get; }
}