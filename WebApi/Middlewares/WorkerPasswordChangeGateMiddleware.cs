using Cable.Core;
using System.Security.Claims;
using Application.Common.Interfaces;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Cable.Middlewares;

/// <summary>
/// WORKER_TRACKER §3.2.3 step 4, enforced on the server: while a worker still holds the temporary password
/// (PartnerCredential.MustChangePassword), a partner-app token may only change the password, refresh, read its
/// assignments or sign out. Everything else answers 403 PASSWORD_CHANGE_REQUIRED, so the forced-change screen in
/// the app is a rule, not a courtesy. Consumer (driver) tokens are never affected.
/// </summary>
internal sealed class WorkerPasswordChangeGateMiddleware(RequestDelegate next)
{
    public const string Code = "PASSWORD_CHANGE_REQUIRED";

    private static readonly PathString[] Allowed =
    [
        new("/api/users/change-my-password"),
        new("/api/users/refresh-access"),
        new("/api/provider/logout"),
        new("/api/workers/me"),
    ];

    public async Task InvokeAsync(HttpContext httpContext, IApplicationDbContext dbContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true
            && int.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            && AuthApps.NormalizeOrDefault(httpContext.User.FindFirstValue(AuthApps.ClaimType)) is AuthApps.Provider or AuthApps.ProviderWeb
            && httpContext.Request.Path.StartsWithSegments("/api")
            && !Allowed.Any(p => httpContext.Request.Path.StartsWithSegments(p)))
        {
            var mustChange = await dbContext.PartnerCredentials.AsNoTracking()
                .AnyAsync(c => c.UserId == userId && !c.IsDeleted && c.MustChangePassword);
            if (mustChange)
                throw new CodedHttpException(StatusCodes.Status403Forbidden, Code,
                    Lang.Pick("لازم تغيّر كلمة السر المؤقتة أول", "You must change your temporary password first"));
        }

        await next(httpContext);
    }
}

public static class WorkerPasswordChangeGateMiddlewareExtension
{
    /// <summary>Call after UseAccountSessionValidation (the account is known to be valid) and before UseAuthorization.</summary>
    public static IApplicationBuilder UseWorkerPasswordChangeGate(this IApplicationBuilder app) =>
        app.UseMiddleware<WorkerPasswordChangeGateMiddleware>();
}
