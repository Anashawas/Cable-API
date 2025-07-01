using System.Security.Claims;
using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Models.Results;
using Cable.Core;
using Cable.Core.Extenstions;
using Cable.Core.Helpers;
using Cable.Security.Encryption.Interfaces;
using Cable.Security.Encryption.Models;
using Cable.Security.Jwt.Interfaces;
using Domain.Enitites;
using Google.Apis.Auth;
using Infrastructrue.Common.Localization;
using Infrastructrue.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Identity;

public class AuthenticationService : IAuthenticationService
{
    private readonly IApplicationDbContext _applicationDbContext;
    private readonly IIdentityService _identityService;
    private readonly ITokenGenerationService _tokenGenerationService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TokenOptions _tokenSettings;
    private readonly GoogleOption _googleOptions;

    private readonly IFirebaseService _firebaseService;


    public AuthenticationService
    (
        IOptions<TokenOptions> tokenSettingsOptions,
        IApplicationDbContext applicationDbContext,
        IIdentityService identityService,
        ITokenGenerationService tokenGenerationService,
        IPasswordHasher passwordHasher,
        IOptions<GoogleOption> googleOptions,
        INotificationService notificationService, IFirebaseService firebaseService)
    {
        _applicationDbContext = applicationDbContext;
        _identityService = identityService;
        _tokenGenerationService = tokenGenerationService;
        _passwordHasher = passwordHasher;
        _firebaseService = firebaseService;
        _tokenSettings = tokenSettingsOptions.Value;
        _googleOptions = googleOptions.Value;
    }

    public async Task<UserLoginByTokenResult> LoginByToken(string token, CancellationToken cancellationToken = default)
    {
        var (principal, securityToken) = _tokenGenerationService.DecodeToken(token);
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier).AsInt();

        if (!userId.HasValue)
        {
            throw new UnauthorizedAccessException();
        }

        var loginResult = await Login(userId.Value, cancellationToken);

        return new UserLoginByTokenResult(loginResult.Id, loginResult.Name,
            loginResult.Privileges);
    }


    public async Task<UserLoginResult> LoginFirebaseAsync(FirebaseLoginDetails firebaseLoginDetails,
        CancellationToken cancellationToken)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(firebaseLoginDetails.IdToken);

        // if (_googleOptions.ClientIds is null)
        //     throw new ArgumentNullException(nameof(_googleOptions.ClientIds));
        //
        // var settings = new GoogleJsonWebSignature.ValidationSettings
        // {
        //     Audience = _googleOptions.ClientIds
        // };


        // var payload = await GoogleJsonWebSignature.ValidateAsync(firebaseLoginDetails.IdToken, settings);
        // if (payload is null)
        // {
        //     throw new DataValidationException(nameof(firebaseLoginDetails.IdToken),
        //         Application.Common.Localization.Resources.InvaildGoogleToken);
        // }
        

        // var user = await CheckGoogleUserExist(payload, cancellationToken);

        var payload = await _firebaseService.ValidateFirebaseTokenAsync(firebaseLoginDetails.IdToken, cancellationToken);
        var user = await CheckUserExist(payload, cancellationToken);
        var isCompletedData = CheckUserDetailsCompleted(user);
        return await GetUserLoginDetails(user.Id, user.Name, user.Email, isCompletedData,
            cancellationToken);
    }

    public async Task<UserLoginResult> Login(int id, CancellationToken cancellationToken = default)
    {
        var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == id,
            cancellationToken: cancellationToken);

        CheckAreUserDetailsValid(user);

        var isCompletedData = CheckUserDetailsCompleted(user);
        return await GetUserLoginDetails(user.Id, user.Name, user.Email, isCompletedData,
            cancellationToken);
    }

    public async Task<UserLoginResult> Login(string email, string password,
        CancellationToken cancellationToken = default)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(email);
        ExceptionHelper.ThrowIfNullOrEmpty(password);
        
        var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Email == email,
            cancellationToken);

        CheckAreUserDetailsValid(user);

        if (VerifyPassword(password, user.Password) == PasswordVerificationResult.Failed)
        {
            throw new NotAuthorizedAccessException(Resources.InvalidUserNameOrPassword);
        }

        var isCompletedData = CheckUserDetailsCompleted(user);
        return await GetUserLoginDetails(user.Id, user.Name, user.Email, isCompletedData,
            cancellationToken);
    }

    public async Task<(string accessToken, string refreshToken)> RefreshTokens(string refreshToken)
    {
        try
        {
            var (principal, securityToken) = _tokenGenerationService.DecodeToken(refreshToken);
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier).AsInt();

            if (!userId.HasValue)
            {
                throw new UnauthorizedAccessException();
            }

            var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == userId.Value);

            CheckAreUserDetailsValid(user);

            return GenerateTokens(userId.Value);
        }
        catch (Exception)
        {
            throw new NotAuthorizedAccessException("");
        }
    }


    private (string accessToken, string refreshToken) GenerateTokens(int userId)
    {
        var (accessToken, _) = _tokenGenerationService.GenerateToken(new System.Security.Claims.Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, _tokenSettings.AccessTokenExpiresAfter);

        var (refreshToken, _) = _tokenGenerationService.GenerateToken(new System.Security.Claims.Claim[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, _tokenSettings.RefreshTokenExpiresAfter);

        return (accessToken, refreshToken);
    }

    private PasswordVerificationResult VerifyPassword(string inputPassword, string hashedPassword)
    {
        return _passwordHasher.VerifyHashedPassword(inputPassword, hashedPassword);
    }

    private async Task<UserLoginResult> GetUserLoginDetails(int id, string name, string email,
        bool isCompletedData,
        CancellationToken cancellationToken = default)
    {
        var (accessToken, refreshToken) = GenerateTokens(id);

        return new UserLoginResult(id, name, email, accessToken, refreshToken,
            isCompletedData,
            await _identityService.GetPrivileges(id, cancellationToken)
        );
    }

    private void CheckAreUserDetailsValid(UserAccount user)
    {
        if (user == null || user.IsDeleted)
        {
            throw new NotAuthorizedAccessException(Resources.InvalidUserNameOrPassword);
        }

        if (!user.IsActive)
        {
            throw new NotAuthorizedAccessException(Resources.DeactivatedUser);
        }
    }

    private bool CheckUserDetailsCompleted(UserAccount user)
        => !string.IsNullOrEmpty(user.Country) && !string.IsNullOrEmpty(user.City) && !string.IsNullOrEmpty(user.Phone);

    private async Task<UserAccount> CheckUserExist(FirebaseTokenValidationResult payload,
        CancellationToken cancellationToken)
    {
        var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x =>
                x.Email == payload.Email && x.FirebaseUId == payload.FirebaseUId  && !x.IsDeleted,
            cancellationToken);
    
        if (user is null)
        {
            user = new()
            {
                Name = payload.Name,
                Email = payload.Email,
                FirebaseUId = payload.FirebaseUId,
                RoleId = 3,
                RegistrationProvider = payload.RegistrationProvider,
                IsActive = true,
                IsDeleted = false
            };
            _applicationDbContext.UserAccounts.Add(user);
            await _applicationDbContext.SaveChanges(cancellationToken);
        }
    
        return user;
    }
    
    
    // private async Task<UserAccount> CheckGoogleUserExist(GoogleJsonWebSignature.Payload payload,
    //     CancellationToken cancellationToken)
    // {
    //     var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x =>
    //             x.Email == payload.Email && x.IsGoogleLogin == true && x.GoogleId == payload.Subject && !x.IsDeleted,
    //         cancellationToken);
    //
    //     if (user is null)
    //     {
    //         user = new()
    //         {
    //             Name = payload.Name,
    //             UserName = payload.GivenName + payload.FamilyName,
    //             Email = payload.Email,
    //             GoogleId = payload.Subject,
    //             RoleId = 3,
    //             IsGoogleLogin = true,
    //             IsActive = true,
    //             IsDeleted = false
    //         };
    //         _applicationDbContext.UserAccounts.Add(user);
    //         await _applicationDbContext.SaveChanges(cancellationToken);
    //     }
    //
    //     return user;
    // }
}