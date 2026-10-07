using System.Security.Claims;
using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Constants;
using Microsoft.EntityFrameworkCore;

namespace Cable.Middlewares;

/// <summary>
/// Revalidates the caller's account on every authenticated request.
///
/// The JWT is the only thing carrying identity: <c>CurrentUserService.UserId</c>
/// reads the NameIdentifier claim and never touches the database. Without this
/// guard a token stays usable for its full lifetime (10 days) after the account
/// behind it is deleted or deactivated — the holder keeps browsing, scanning
/// partner codes and moving loyalty points, because the ~118 handlers that act
/// on the caller's id have no reason to re-check it and most do not.
///
/// Two independent checks share one query:
///  - Account state: the account must still exist, not be soft-deleted, and be
///    active. Runs for EVERY authenticated request.
///  - Session stamp: per client app, so the consumer app, the provider app and
///    the partner web portal hold independent sessions for one account. Runs
///    only when the token carries a stamp.
/// </summary>
internal sealed class AccountSessionValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, IApplicationDbContext dbContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true
            && int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            // Tokens issued before per-app stamps shipped carry no "app" claim
            // and are validated as consumer, so existing sessions survive a deploy.
            var app = AuthApps.NormalizeOrDefault(httpContext.User.FindFirstValue(AuthApps.ClaimType));

            var account = await dbContext.UserAccounts
                .AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => new
                {
                    x.IsDeleted,
                    x.IsActive,
                    x.SecurityStamp,
                    x.ProviderSecurityStamp,
                    x.ProviderWebSecurityStamp
                })
                .FirstOrDefaultAsync();

            // Deliberately NOT gated on the token carrying a stamp. The stamp
            // check below is, because a token without one has nothing to compare
            // — but account state has to hold for every caller, or a pre-stamp
            // token would walk straight past the very check this exists for.
            if (account is null || account.IsDeleted)
                throw new NotAuthorizedAccessException("This account is no longer available.");

            if (!account.IsActive)
                throw new NotAuthorizedAccessException("This account has been deactivated.");

            var tokenStamp = httpContext.User.FindFirstValue("SecurityStamp");
            if (!string.IsNullOrEmpty(tokenStamp))
            {
                var currentStamp = app switch
                {
                    AuthApps.Provider => account.ProviderSecurityStamp,
                    AuthApps.ProviderWeb => account.ProviderWebSecurityStamp,
                    _ => account.SecurityStamp,
                };

                if (currentStamp != null && currentStamp != tokenStamp)
                    throw new NotAuthorizedAccessException("Session expired. You have been logged in on another device.");
            }
        }

        await next(httpContext);
    }
}

public static class AccountSessionValidationMiddlewareExtension
{
    /// <summary>
    /// Rejects requests whose account has been deleted or deactivated, and whose
    /// session stamp no longer matches. Call AFTER UseAuthentication and BEFORE
    /// UseAuthorization / UseSecureFileServing so no handler — and no protected
    /// file download — runs for a revoked account.
    /// </summary>
    public static IApplicationBuilder UseAccountSessionValidation(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AccountSessionValidationMiddleware>();
    }
}
