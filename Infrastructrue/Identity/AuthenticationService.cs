using System.Security.Claims;
using Application.Common.Interfaces;
using Application.Common.Models;
using Cable.Core;
using Cable.Core.Extenstions;
using Cable.Core.Helpers;
using Cable.Security.Encryption.Interfaces;
using Cable.Security.Encryption.Models;
using Cable.Security.Jwt.Interfaces;
using Domain.Enitites;
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

    public AuthenticationService
        (
        IOptions<TokenOptions> tokenSettingsOptions,
        IApplicationDbContext applicationDbContext,

        IIdentityService identityService,
        ITokenGenerationService tokenGenerationService,
        IPasswordHasher passwordHasher
        )
    {
        _applicationDbContext = applicationDbContext;
        _identityService = identityService;
        _tokenGenerationService = tokenGenerationService;
        _passwordHasher = passwordHasher;
        _tokenSettings = tokenSettingsOptions.Value;
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

        return new UserLoginByTokenResult(loginResult.Id, loginResult.Username, loginResult.Name,  loginResult.Privileges);
    }

    public async Task<UserLoginResult> Login(int id, CancellationToken cancellationToken = default)
    {
        var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken: cancellationToken);

        CheckAreUserDetailsValid(user);

        return await GetUserLoginDetails(user.Id, user.UserName, user.Name, cancellationToken);
    }

    public async Task<UserLoginResult> Login(string username, string password, CancellationToken cancellationToken = default)
    {
        ExceptionHelper.ThrowIfNullOrEmpty(username);
        ExceptionHelper.ThrowIfNullOrEmpty(password);

        var user = await _applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.UserName == username,cancellationToken);

        CheckAreUserDetailsValid(user);


            if (VerifyPassword(password, user.Password) == PasswordVerificationResult.Failed)
            {
                throw new NotAuthorizedAccessException(Resources.InvalidUserNameOrPassword);
            }
          

        

        return await GetUserLoginDetails(user.Id, user.UserName,  user.Name,  cancellationToken);
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
            new Claim(ClaimTypes.NameIdentifier,userId.ToString())
         }, _tokenSettings.AccessTokenExpiresAfter);

        var (refreshToken, _) = _tokenGenerationService.GenerateToken(new System.Security.Claims.Claim[]
         {
            new Claim(ClaimTypes.NameIdentifier,userId.ToString())
         }, _tokenSettings.RefreshTokenExpiresAfter);

        return (accessToken, refreshToken);
    }

    private PasswordVerificationResult VerifyPassword(string inputPassword, string hasedPassword)
    {
        return _passwordHasher.VerifyHashedPassword(inputPassword, hasedPassword);
    }

    private async Task<UserLoginResult> GetUserLoginDetails(int id, string userName, string name, CancellationToken cancellationToken = default)
    {
        var (accessToken, refreshToken) = GenerateTokens(id);

        return new UserLoginResult(id, userName, name,  accessToken, refreshToken,
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



  

}
