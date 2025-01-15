using Cable.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cable.WebApi.Middlewares;

internal sealed class UnAuthenticatedApiHandlerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        await next(httpContext);
        switch (httpContext.Response.StatusCode)
        {
            case (int)StatusCodes.Status401Unauthorized:
                throw new NotAuthorizedAccessException();
            case (int)StatusCodes.Status403Forbidden:
                throw new ForbiddenAccessException();
        }
    }
}
public static class UnAuthenticatedApiHandlerMiddlewareExtension
{
    /// <summary>
    /// Registers a middleware that throws NotAuthorizedAccessException or ForbiddenAccessException based on the .Net authentication/authorization middlewars
    /// </summary>
    /// <param name="app"></param>
    /// <returns></returns>
    public static IApplicationBuilder UseCustomAuthenticationResponse(this IApplicationBuilder app)
    {
        return app.UseMiddleware<UnAuthenticatedApiHandlerMiddleware>();
    }
}
