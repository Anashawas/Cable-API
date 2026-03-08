using System.Security.Claims;
using Application.Common.Interfaces;
using Cable.Core;
using Microsoft.EntityFrameworkCore;

namespace Cable.Middlewares;

internal sealed class SecurityStampValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, IApplicationDbContext dbContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var tokenStamp = httpContext.User.FindFirstValue("SecurityStamp");
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrEmpty(tokenStamp) && !string.IsNullOrEmpty(userId))
            {
                var currentStamp = await dbContext.UserAccounts
                    .AsNoTracking()
                    .Where(x => x.Id == int.Parse(userId))
                    .Select(x => x.SecurityStamp)
                    .FirstOrDefaultAsync();

                if (currentStamp != null && currentStamp != tokenStamp)
                {
                    throw new NotAuthorizedAccessException("Session expired. You have been logged in on another device.");
                }
            }
        }

        await next(httpContext);
    }
}

public static class SecurityStampValidationMiddlewareExtension
{
    public static IApplicationBuilder UseSecurityStampValidation(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityStampValidationMiddleware>();
    }
}
