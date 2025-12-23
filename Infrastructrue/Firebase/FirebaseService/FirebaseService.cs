using Application.Common.Interfaces;
using Application.Common.Models.Results;
using Cable.Core.Exceptions;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Infrastructrue.Common.Models.Results;
using infrastructrue.Options;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Firebase.FirebaseService;

public class FirebaseService : IFirebaseService
{
    private readonly FirebaseOption _firebaseOption;

    public FirebaseMessaging FirebaseMessaging {  get;  }

    public FirebaseService(IOptions<FirebaseOption> firebaseOption)
    {
        _firebaseOption = firebaseOption.Value;

        if (FirebaseApp.DefaultInstance == null)
        {
            FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    firebaseOption.Value.ServiceAccountPath)),
            });
        }

        FirebaseMessaging = FirebaseMessaging.DefaultInstance;
    }

    public async Task<FirebaseTokenValidationResult> ValidateFirebaseTokenAsync(string idToken,
        CancellationToken cancellationToken = default)
    {
        var decodedToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance
            .VerifyIdTokenAsync(idToken, cancellationToken);
        var userDetails =
            await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance.GetUserAsync(decodedToken.Uid, cancellationToken)
            ?? throw new NotFoundException("Firebase User", decodedToken.Uid);

        
        return new(
            userDetails.Uid,
            userDetails.ProviderData.FirstOrDefault()?.ProviderId,
            userDetails.Email,
            userDetails.DisplayName
        );
    }
}