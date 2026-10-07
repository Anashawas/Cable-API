using System.Security.Claims;
using Application.Common.Extensions;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Models;
using Application.Common.Models.Results;
using Application.Users.Queries.GetAllUsers;
using Application.Users.Queries.GetUserById;
using Cable.Core;
using Cable.Core.Constants;
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
            // Pre-existing tokens carry no "app" claim and default to consumer.
            var app = AuthApps.NormalizeOrDefault(principal.FindFirstValue(AuthApps.ClaimType));

            if (!userId.HasValue)
            {
                throw new UnauthorizedAccessException();
            }

            var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == userId.Value);

            CheckAreUserDetailsValid(user);

            // Validate against the stamp for THIS app — refreshing a provider
            // token must not be judged against the consumer app's session.
            var currentStamp = GetStampForApp(user, app);

            if (!string.IsNullOrEmpty(currentStamp) && currentStamp != tokenStamp)
                throw new NotAuthorizedAccessException("Session expired. You have been logged in on another device.");

            return GenerateTokens(userId.Value, currentStamp ?? "", app);
        }
        catch (NotAuthorizedAccessException) { throw; }
        catch (Exception)
        {
            throw new NotAuthorizedAccessException("");
        }
    }


    private (string accessToken, string refreshToken) GenerateTokens(int userId, string securityStamp,
        string app = AuthApps.Consumer)
    {
        // The "app" claim is additive — existing clients treat the token as an
        // opaque string, so tagging it needs no mobile release.
        var (accessToken, _) = _tokenGenerationService.GenerateToken([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("SecurityStamp", securityStamp),
            new Claim(AuthApps.ClaimType, app)
        ], _tokenSettings.AccessTokenExpiresAfter);

        var (refreshToken, _) = _tokenGenerationService.GenerateToken([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("SecurityStamp", securityStamp),
            new Claim(AuthApps.ClaimType, app)
        ], _tokenSettings.RefreshTokenExpiresAfter);

        return (accessToken, refreshToken);
    }

    private PasswordVerificationResult VerifyPassword(string inputPassword, string? hashedPassword)
    {
        // Accounts created through Google/Apple — and phone+OTP registrations —
        // have no local password. The hasher throws ArgumentNullException on a
        // null hash, and that type is not mapped in ExceptionHandlerMiddleware,
        // so it escaped as a 500 instead of a plain "wrong credentials" 401.
        // Deliberately returns Failed rather than a distinct error: telling the
        // caller "this account has no password" would leak which accounts exist
        // and which sign-in method they use.
        if (string.IsNullOrEmpty(hashedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        return _passwordHasher.VerifyHashedPassword(inputPassword, hashedPassword);
    }

    private async Task<UserLoginResult> GetUserLoginDetails(UserAccount userAccount,
        bool isCompletedData,
        CancellationToken cancellationToken = default, bool rotateStamp = true,
        string app = AuthApps.Consumer)
    {
        // Each app owns its own stamp column, so signing into one app leaves the
        // other apps' sessions intact. Within a single app the original
        // single-device behaviour is preserved.
        var currentStamp = GetStampForApp(userAccount, app);

        var securityStamp = rotateStamp
            ? await RotateSecurityStamp(userAccount.Id, cancellationToken, app)
            : currentStamp ?? "";

        var (accessToken, refreshToken) = GenerateTokens(userAccount.Id, securityStamp, app);

        // Every sign-in path — password, Firebase, OTP, provider, token — funnels
        // through here, so this one write covers all of them. LastSeenAt is set
        // too: the login itself is activity, and waiting for the next request
        // would leave a user who signs in and closes the app looking inactive.
        var now = DateTime.UtcNow;
        var isPartnerClient = app != AuthApps.Consumer;
        await _applicationDbContext.UserAccounts
            .Where(x => x.Id == userAccount.Id)
            .ExecuteUpdateAsync(x => x
                    .SetProperty(u => u.LastLoginAt, now)
                    .SetProperty(u => u.LastSeenAt, now)
                    // Partner-scoped twins: only the provider app and the partner
                    // web portal move these. The conditional becomes CASE WHEN,
                    // so a consumer login is still one UPDATE.
                    .SetProperty(u => u.PartnerLastLoginAt, u => isPartnerClient ? now : u.PartnerLastLoginAt)
                    .SetProperty(u => u.PartnerLastSeenAt, u => isPartnerClient ? now : u.PartnerLastSeenAt),
                cancellationToken);

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

    /// <summary>
    /// True when the account still carries a social identity (Google / Apple).
    /// Promoting a user to Provider or Worker clears these — see
    /// UpdateUserCommand — so a provider account never reaches the partner apps
    /// with one still attached.
    /// </summary>
    private static bool IsSocialAccount(UserAccount user)
        => !string.IsNullOrEmpty(user.RegistrationProvider)
           || !string.IsNullOrEmpty(user.FirebaseUId);

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

        // Provider-app access is granted to Providers (owners) and Workers (assigned staff).
        var allowedRoleIds = await _applicationDbContext.Roles
            .Where(r => (r.Name == "Provider" || r.Name == "Worker") && !r.IsDeleted)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (allowedRoleIds.Count == 0)
        {
            throw new CableApplicationException("Provider/Worker roles not configured in the system");
        }

        if (!allowedRoleIds.Contains(user.RoleId))
        {
            throw new ForbiddenAccessException("Access denied. This endpoint is only for Provider or Worker users.");
        }

        // Provider access is restricted to regular email + password accounts.
        // A social account can still acquire a password (the forgot-password
        // flow does not exclude them), which would otherwise let a Google/Apple
        // identity through this endpoint. Checked AFTER password verification so
        // an unauthenticated caller cannot probe which sign-in method an email uses.
        //
        // Reaching this with a social identity attached means the account was
        // never converted on promotion — UpdateUserCommand clears those fields
        // when a user is granted Provider or Worker, so this should not trigger
        // for anyone promoted through the admin portal.
        //
        // Uses DataValidationException (not ForbiddenAccessException) because the
        // forbidden handler discards ex.Message and returns generic text — the
        // caller would get "not allowed" with no reason. This matches how the
        // phone-missing check below reports an actionable message.
        if (IsSocialAccount(user))
        {
            throw new DataValidationException("Email",
                "This account signs in with Google or Apple. Provider access requires " +
                "a regular email and password account. Please contact the administrator.");
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
        CancellationToken cancellationToken = default,
        string app = AuthApps.Provider)
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

        // Provider-family session (mobile app or web portal, per the caller's
        // resolved app) — rotates only that app's stamp, leaving the Cable
        // consumer app and the other provider surface untouched.
        return await GetUserLoginDetails(user, isCompletedData, cancellationToken,
            app: app);
    }

    #endregion

    #region Session Management

    public async Task Logout(int userId, CancellationToken cancellationToken = default,
        string app = AuthApps.Consumer)
    {
        // Ends only the calling app's session; the other app stays signed in.
        await RotateSecurityStamp(userId, cancellationToken, app);
    }

    /// <summary>The stored session stamp matching the app a token was issued to.</summary>
    private static string? GetStampForApp(UserAccount user, string app) => app switch
    {
        AuthApps.Provider => user.ProviderSecurityStamp,
        AuthApps.ProviderWeb => user.ProviderWebSecurityStamp,
        _ => user.SecurityStamp,
    };

    private async Task<string> RotateSecurityStamp(int userId, CancellationToken cancellationToken,
        string app = AuthApps.Consumer)
    {
        var newStamp = Guid.NewGuid().ToString("N");

        // Rotate only the stamp belonging to the app that is signing in/out, so
        // the other apps' active sessions survive.
        var query = _applicationDbContext.UserAccounts.Where(x => x.Id == userId);

        switch (app)
        {
            case AuthApps.Provider:
                await query.ExecuteUpdateAsync(x => x.SetProperty(u => u.ProviderSecurityStamp, newStamp), cancellationToken);
                break;
            case AuthApps.ProviderWeb:
                await query.ExecuteUpdateAsync(x => x.SetProperty(u => u.ProviderWebSecurityStamp, newStamp), cancellationToken);
                break;
            default:
                await query.ExecuteUpdateAsync(x => x.SetProperty(u => u.SecurityStamp, newStamp), cancellationToken);
                break;
        }

        return newStamp;
    }

    #endregion
}