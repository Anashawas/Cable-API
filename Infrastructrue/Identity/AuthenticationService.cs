using System.Security.Claims;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Models;
using Application.Common.Models.Results;
using Application.Users.Queries.GetAllUsers;
using Application.Users.Queries.GetUserById;
using Cable.Core;
using Cable.Core.Extenstions;
using Cable.Core.Helpers;
using Cable.Core.Utilities;
using Cable.Security.Encryption.Interfaces;
using Cable.Security.Encryption.Models;
using Cable.Security.Jwt.Interfaces;
using Domain.Enitites;
using Infrastructrue.Common.Localization;
using Infrastructrue.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Identity;

public class AuthenticationService : IAuthenticationService
{
    private readonly IApplicationDbContext _applicationDbContext;
    private readonly IIdentityService _identityService;
    private readonly ITokenGenerationService _tokenGenerationService;
    private readonly IUserAccountRepository   _userAccountRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TokenOptions _tokenSettings;
    private readonly GoogleOption _googleOptions;
    private readonly IFirebaseService _firebaseService;
    private readonly IOtpService _otpService;
    private readonly HybridCache _cache;


    public AuthenticationService
    (
        IOptions<TokenOptions> tokenSettingsOptions,
        IApplicationDbContext applicationDbContext,
        IIdentityService identityService,
        ITokenGenerationService tokenGenerationService,
        IPasswordHasher passwordHasher,
        IOptions<GoogleOption> googleOptions,
        INotificationService notificationService,
        IFirebaseService firebaseService,
        IUserAccountRepository userAccountRepository,
        IOtpService otpService,
        HybridCache cache)
    {
        _applicationDbContext = applicationDbContext;
        _identityService = identityService;
        _tokenGenerationService = tokenGenerationService;
        _passwordHasher = passwordHasher;
        _firebaseService = firebaseService;
        _userAccountRepository = userAccountRepository;
        _otpService = otpService;
        _cache = cache;
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

        return new UserLoginByTokenResult(loginResult.UserDetails.Id, loginResult.UserDetails.Name,
            loginResult.Privileges);
    }


    public async Task<UserLoginResult> LoginFirebaseAsync(FirebaseLoginDetails firebaseLoginDetails,
        CancellationToken cancellationToken)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(firebaseLoginDetails.IdToken);
        
        var payload = await _firebaseService.ValidateFirebaseTokenAsync(firebaseLoginDetails.IdToken, cancellationToken:cancellationToken);
        var user = await CheckUserExist(payload, cancellationToken);
        var isCompletedData = CheckUserDetailsCompleted(user);
        var userDetails = await _userAccountRepository.GetUserDetailsByIdAsync( user.Id, cancellationToken);
        
        return await GetUserLoginDetails(userDetails, isCompletedData,
            cancellationToken);
    }

    public async Task<UserLoginResult> Login(int id, CancellationToken cancellationToken = default)
    {
        var user = await _userAccountRepository.GetUserDetailsByIdAsync(id, cancellationToken);

        CheckAreUserDetailsValid(user);

        var isCompletedData = CheckUserDetailsCompleted(user);

        return await GetUserLoginDetails(user, isCompletedData, cancellationToken, rotateStamp: false);
    }

    public async Task<UserLoginResult> Login(string email, string password,
        CancellationToken cancellationToken = default)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(email);
        ExceptionHelper.ThrowIfNullOrEmpty(password);
        var user = await _userAccountRepository.GetUserDetailsByEmailAsync(email, cancellationToken);
       

        CheckAreUserDetailsValid(user);

        if (VerifyPassword(password, user.Password) == PasswordVerificationResult.Failed)
        {
            throw new NotAuthorizedAccessException(Resources.InvalidUserNameOrPassword);
        }

        var isCompletedData = CheckUserDetailsCompleted(user);
        
        return await GetUserLoginDetails(user, isCompletedData, cancellationToken);
    }

    public async Task<(string accessToken, string refreshToken)> RefreshTokens(string refreshToken)
    {
        try
        {
            var (principal, securityToken) = _tokenGenerationService.DecodeToken(refreshToken);
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier).AsInt();
            var tokenStamp = principal.FindFirstValue("SecurityStamp");

            if (!userId.HasValue)
            {
                throw new UnauthorizedAccessException();
            }

            var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == userId.Value);

            CheckAreUserDetailsValid(user);

            // Validate SecurityStamp — mismatch means user logged in on another device
            if (!string.IsNullOrEmpty(user.SecurityStamp) && user.SecurityStamp != tokenStamp)
                throw new NotAuthorizedAccessException("Session expired. You have been logged in on another device.");

            return GenerateTokens(userId.Value, user.SecurityStamp ?? "");
        }
        catch (NotAuthorizedAccessException) { throw; }
        catch (Exception)
        {
            throw new NotAuthorizedAccessException("");
        }
    }


    private (string accessToken, string refreshToken) GenerateTokens(int userId, string securityStamp)
    {
        var (accessToken, _) = _tokenGenerationService.GenerateToken([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("SecurityStamp", securityStamp)
        ], _tokenSettings.AccessTokenExpiresAfter);

        var (refreshToken, _) = _tokenGenerationService.GenerateToken([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("SecurityStamp", securityStamp)
        ], _tokenSettings.RefreshTokenExpiresAfter);

        return (accessToken, refreshToken);
    }

    private PasswordVerificationResult VerifyPassword(string inputPassword, string hashedPassword)
    {
        return _passwordHasher.VerifyHashedPassword(inputPassword, hashedPassword);
    }

    private async Task<UserLoginResult> GetUserLoginDetails(UserAccount userAccount,
        bool isCompletedData,
        CancellationToken cancellationToken = default, bool rotateStamp = true)
    {
        var securityStamp = rotateStamp
            ? await RotateSecurityStamp(userAccount.Id, cancellationToken)
            : userAccount.SecurityStamp ?? "";

        var (accessToken, refreshToken) = GenerateTokens(userAccount.Id, securityStamp);

        var userDetails = userAccount.ToUserDetails();
        return new UserLoginResult(userDetails, accessToken, refreshToken,
            isCompletedData,
            await _identityService.GetPrivileges(userAccount.Id, cancellationToken)
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
        => !string.IsNullOrEmpty(user.Country) && !string.IsNullOrEmpty(user.City) ;

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
    
    public async Task<string> SendOtpAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(phoneNumber);
        
        // Normalize phone number to standard format
        var normalizedPhoneNumber = Cable.Core.Utilities.PhoneNumberUtility.NormalizePhoneNumber(phoneNumber);
        if (normalizedPhoneNumber == null)
        {
            throw new DataValidationException("PhoneNumber", "Invalid phone number format. Please use a valid Jordan mobile number.");
        }
        
        if (await _otpService.IsRateLimitedAsync(normalizedPhoneNumber, cancellationToken))
        {
            throw new DataValidationException("MaxRequestsPerWindow","Rate limit exceeded. Please try again later.");
        }

        var otp = await _otpService.GenerateOtpAsync(normalizedPhoneNumber, cancellationToken);
        var sent = await _otpService.SendOtpAsync(normalizedPhoneNumber, otp, cancellationToken);
        
        if (!sent)
        {
            throw new CableApplicationException("Failed to send OTP. Please try again.");
        }
        
        return "OTP sent successfully";
    }

    public async Task<UserLoginResult> LoginWithOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(phoneNumber);
        ExceptionHelper.ThrowIfNullOrEmpty(otp);
        
        // Normalize phone number to standard format
        var normalizedPhoneNumber = PhoneNumberUtility.NormalizePhoneNumber(phoneNumber);
        if (normalizedPhoneNumber == null)
        {
            throw new DataValidationException("PhoneNumber", "Invalid phone number format. Please use a valid Jordan mobile number.");
        }
        
        var isValid = await _otpService.VerifyOtpAsync(normalizedPhoneNumber, otp, cancellationToken);
        if (!isValid)
        {
            throw new NotAuthorizedAccessException("Invalid or expired OTP");
        }

        // Find or create user by normalized phone number
        var user = await _applicationDbContext.UserAccounts
            .Include(x => x.Role)
            .Include(x => x.UserCars)
                .ThenInclude(x => x.CarModel)
                    .ThenInclude(x => x.CarType)
            .Include(x => x.UserCars)
                .ThenInclude(x => x.PlugType)
            .FirstOrDefaultAsync(x => x.Phone == normalizedPhoneNumber && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            // Create new user with normalized phone number
            user = new UserAccount
            {
                Phone = normalizedPhoneNumber, // Store normalized format
                RoleId = 3, 
                IsActive = true,
                IsDeleted = false,
                IsPhoneVerified = true,
                PhoneVerifiedAt = DateTime.UtcNow,
                Name = null,
                Email = null
                
            };
            
            _applicationDbContext.UserAccounts.Add(user);
            await _applicationDbContext.SaveChanges(cancellationToken);
        }
        else
        {
            user.IsPhoneVerified = true;
            user.PhoneVerifiedAt = DateTime.UtcNow;
            await _applicationDbContext.SaveChanges(cancellationToken);
        }

        CheckAreUserDetailsValid(user);
        var isCompletedData = CheckUserDetailsCompleted(user);

        return await GetUserLoginDetails(user, isCompletedData, cancellationToken);
    }

    #region Provider 2FA Authentication

    public async Task<ProviderAuthSessionResult> LoginProvider(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(email);
        ExceptionHelper.ThrowIfNullOrEmpty(password);

        var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Email == email && !x.IsDeleted && x.IsActive, cancellationToken);

        CheckAreUserDetailsValid(user);

        var passwordVerification = VerifyPassword(password, user.Password);
        if (passwordVerification == PasswordVerificationResult.Failed)
        {
            throw new NotAuthorizedAccessException(Resources.InvalidUserNameOrPassword);
        }

        var providerRole = await _applicationDbContext.Roles
            .FirstOrDefaultAsync(r => r.Name == "Provider" && !r.IsDeleted, cancellationToken);

        if (providerRole == null)
        {
            throw new CableApplicationException("Provider role not configured in the system");
        }

        if (user.RoleId != providerRole.Id)
        {
            throw new ForbiddenAccessException("Access denied. This endpoint is only for Provider users.");
        }


        if (string.IsNullOrEmpty(user.Phone))
        {
            throw new DataValidationException("Phone", "Phone number is required for Provider authentication. Please contact administrator to add phone number.");
        }


        var sessionToken = Guid.NewGuid().ToString("N");


        var sessionData = new ProviderAuthSession
        {
            UserId = user.Id,
            PhoneNumber = user.Phone,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };

        var cacheKey = $"provider-auth:{sessionToken}";
        await _cache.SetAsync(cacheKey, sessionData, new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(10),
            LocalCacheExpiration = TimeSpan.FromMinutes(10)
        }, cancellationToken: cancellationToken);

        // Mask phone for display (show last 4 digits)
        var phoneMasked = user.Phone.Length > 4
            ? "****" + user.Phone.Substring(user.Phone.Length - 4)
            : user.Phone;

        return new ProviderAuthSessionResult(
            Success: true,
            Message: "Email and password verified. Please proceed to verify OTP.",
            SessionToken: sessionToken,
            PhoneMasked: phoneMasked,
            ExpiresAt: sessionData.ExpiresAt
        );
    }

    public async Task<string> SendProviderOtpAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(sessionToken);

        // Get session data from cache
        var cacheKey = $"provider-auth:{sessionToken}";
        var sessionData = await _cache.GetOrCreateAsync<ProviderAuthSession>(
            cacheKey,
            async cancel => throw new NotAuthorizedAccessException("Invalid or expired session token"),
            cancellationToken: cancellationToken);

        if (sessionData == null || sessionData.ExpiresAt < DateTime.UtcNow)
        {
            throw new NotAuthorizedAccessException("Session expired. Please login again.");
        }


        var user = await _applicationDbContext.UserAccounts
            .FirstOrDefaultAsync(x => x.Id == sessionData.UserId && !x.IsDeleted, cancellationToken);

        CheckAreUserDetailsValid(user);


        if (await _otpService.IsRateLimitedAsync(sessionData.PhoneNumber, cancellationToken))
        {
            throw new DataValidationException("RateLimit", "Rate limit exceeded. Please try again later.");
        }

        var otp = await _otpService.GenerateOtpAsync(sessionData.PhoneNumber, cancellationToken);
        var sent = await _otpService.SendOtpAsync(sessionData.PhoneNumber, otp, cancellationToken);

        if (!sent)
        {
            throw new CableApplicationException("Failed to send OTP. Please try again.");
        }

        return "OTP sent successfully to your registered phone number";
    }

    public async Task<UserLoginResult> VerifyProviderOtpAsync(
        string sessionToken,
        string otp,
        CancellationToken cancellationToken = default)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(sessionToken);
        ExceptionHelper.ThrowIfNullOrEmpty(otp);

        // Get session data from cache
        var cacheKey = $"provider-auth:{sessionToken}";
        var sessionData = await _cache.GetOrCreateAsync<ProviderAuthSession>(
            cacheKey,
            async cancel => throw new NotAuthorizedAccessException("Invalid or expired session token"),
            cancellationToken: cancellationToken);

        if (sessionData == null || sessionData.ExpiresAt < DateTime.UtcNow)
        {
            throw new NotAuthorizedAccessException("Session expired. Please login again.");
        }

        // Verify OTP
        var isValid = await _otpService.VerifyOtpAsync(sessionData.PhoneNumber, otp, cancellationToken);
        if (!isValid)
        {
            throw new NotAuthorizedAccessException("Invalid or expired OTP");
        }

        // Remove session from cache after successful verification (one-time use)
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        // Get user details and complete login
        var user = await _userAccountRepository.GetUserDetailsByIdAsync(sessionData.UserId, cancellationToken);

        CheckAreUserDetailsValid(user);

        var isCompletedData = CheckUserDetailsCompleted(user);

        return await GetUserLoginDetails(user, isCompletedData, cancellationToken);
    }

    #endregion

    #region Session Management

    public async Task Logout(int userId, CancellationToken cancellationToken = default)
    {
        await RotateSecurityStamp(userId, cancellationToken);
    }

    private async Task<string> RotateSecurityStamp(int userId, CancellationToken cancellationToken)
    {
        var newStamp = Guid.NewGuid().ToString("N");
        await _applicationDbContext.UserAccounts
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.SecurityStamp, newStamp), cancellationToken);
        return newStamp;
    }

    #endregion
}