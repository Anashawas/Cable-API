using Application.Common.Interfaces;
using Application.Common.Models.Results;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using infrastructrue.Options;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Firebase.FirebaseService;

public class FirebaseService : IFirebaseService
{
    private readonly FirebaseOption _firebaseOption;
    private readonly Dictionary<FirebaseAppType, FirebaseApp> _firebaseApps;
    private readonly Dictionary<FirebaseAppType, FirebaseMessaging> _firebaseMessagingInstances;

    public FirebaseService(IOptions<FirebaseOption> firebaseOption)
    {
        _firebaseOption = firebaseOption.Value;
        _firebaseApps = new Dictionary<FirebaseAppType, FirebaseApp>();
        _firebaseMessagingInstances = new Dictionary<FirebaseAppType, FirebaseMessaging>();

       
        InitializeFirebaseApp(
            FirebaseAppType.UserApp,
            _firebaseOption.UserAppServiceAccountPath,
            "cable-user-app");

        InitializeFirebaseApp(
            FirebaseAppType.StationApp,
            _firebaseOption.StationAppServiceAccountPath,
            "cable-station-app");
    }

    private void InitializeFirebaseApp(FirebaseAppType appType, string serviceAccountPath, string appName)
    {
        try
        {
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, serviceAccountPath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Firebase service account file not found at: {fullPath}. " +
                    $"Please ensure the service account JSON file exists for {appType}.");
            }

            FirebaseApp app;

            // First app uses default instance, others use named instances
            if (appType == FirebaseAppType.UserApp)
            {
                // Use default instance for User App
                if (FirebaseApp.DefaultInstance == null)
                {
                    app = FirebaseApp.Create(new AppOptions
                    {
                        Credential = GoogleCredential.FromFile(fullPath)
                    });
                }
                else
                {
                    app = FirebaseApp.DefaultInstance;
                }
            }
            else
            {
                // Use named instance for other apps
                app = FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(fullPath)
                }, appName);
            }

            _firebaseApps[appType] = app;
            _firebaseMessagingInstances[appType] = FirebaseMessaging.GetMessaging(app);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to initialize Firebase app '{appName}' for {appType}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets FirebaseApp instance for the specified app type
    /// </summary>
    private FirebaseApp GetFirebaseApp(FirebaseAppType appType)
    {
        if (!_firebaseApps.TryGetValue(appType, out var app))
        {
            throw new InvalidOperationException(
                $"Firebase app not initialized for {appType}. " +
                $"Please check your Firebase configuration in appsettings.json");
        }
        return app;
    }

    /// <summary>
    /// Gets FirebaseMessaging instance for the specified app type
    /// </summary>
    public FirebaseMessaging GetFirebaseMessaging(FirebaseAppType appType = FirebaseAppType.UserApp)
    {
        if (!_firebaseMessagingInstances.TryGetValue(appType, out var messaging))
        {
            throw new InvalidOperationException(
                $"Firebase messaging not initialized for {appType}. " +
                $"Please check your Firebase configuration in appsettings.json");
        }
        return messaging;
    }

    /// <summary>
    /// Validates Firebase ID token from the specified app
    /// </summary>
    public async Task<FirebaseTokenValidationResult> ValidateFirebaseTokenAsync(
        string idToken,
        FirebaseAppType appType = FirebaseAppType.UserApp,
        CancellationToken cancellationToken = default)
    {
        var app = GetFirebaseApp(appType);
        var auth = FirebaseAdmin.Auth.FirebaseAuth.GetAuth(app);

        var decodedToken = await auth.VerifyIdTokenAsync(idToken, cancellationToken);
        var userDetails = await auth.GetUserAsync(decodedToken.Uid, cancellationToken)
            ?? throw new NotFoundException("Firebase User", decodedToken.Uid);

        return new FirebaseTokenValidationResult(
            userDetails.Uid,
            userDetails.ProviderData.FirstOrDefault()?.ProviderId,
            userDetails.Email,
            userDetails.DisplayName
        );
    }
}