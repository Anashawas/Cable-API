using System.Security.Claims;
using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Cable.Core.Constants;

namespace Cable.Middlewares;

/// <summary>
/// Stamps <c>UserAccount.LastSeenAt</c> on authenticated requests — the signal
/// behind DAU/WAU/MAU.
///
/// Writes are throttled to one per user per <see cref="ThrottleWindow"/>. Without
/// that, a single app session would issue an UPDATE for every request it makes,
/// turning a reporting field into a write-amplification problem on the busiest
/// table in the database. The cost is that LastSeenAt can lag reality by up to
/// the window — irrelevant against a reporting period measured in days.
/// </summary>
internal sealed class UserActivityTrackingMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(15);

    public async Task InvokeAsync(HttpContext httpContext, IApplicationDbContext dbContext, IMemoryCache cache)
    {
        // Run the request first. Activity tracking must never be the reason a
        // request fails, and a request that 401s or throws is not activity.
        await next(httpContext);

        if (httpContext.User.Identity?.IsAuthenticated != true)
            return;

        if (httpContext.Response.StatusCode >= 400)
            return;

        var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
            return;

        // Keyed per client kind as well as per user: an owner who is also a
        // driver must not have a partner-app request swallowed by the throttle
        // slot a consumer-app request claimed a minute earlier.
        var isPartnerClient = AuthApps.NormalizeOrDefault(
            httpContext.User.FindFirstValue(AuthApps.ClaimType)) != AuthApps.Consumer;
        var cacheKey = $"last-seen:{userId}:{(isPartnerClient ? "partner" : "consumer")}";
        if (cache.TryGetValue(cacheKey, out _))
            return;

        // Claim the slot before writing, so concurrent requests from the same
        // user collapse to one UPDATE instead of racing.
        cache.Set(cacheKey, true, ThrottleWindow);

        try
        {
            await dbContext.UserAccounts
                .Where(x => x.Id == userId)
                .ExecuteUpdateAsync(x => x
                        .SetProperty(u => u.LastSeenAt, DateTime.UtcNow)
                        .SetProperty(u => u.PartnerLastSeenAt, u => isPartnerClient ? DateTime.UtcNow : u.PartnerLastSeenAt),
                    httpContext.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // Client disconnected mid-request. Nothing to report.
        }
        catch (Exception ex)
        {
            // A telemetry write must never surface to the caller — the response
            // has already been sent by this point regardless.
            httpContext.RequestServices
                .GetService<ILogger<UserActivityTrackingMiddleware>>()?
                .LogWarning(ex, "Failed to update LastSeenAt for user {UserId}", userId);
        }
    }
}

public static class UserActivityTrackingMiddlewareExtension
{
    public static IApplicationBuilder UseUserActivityTracking(this IApplicationBuilder app)
        => app.UseMiddleware<UserActivityTrackingMiddleware>();
}
