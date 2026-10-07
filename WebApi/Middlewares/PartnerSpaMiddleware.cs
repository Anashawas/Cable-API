using Microsoft.Extensions.FileProviders;

namespace Cable.Middlewares;

/// <summary>
/// Serves the Cable Partner SPA (React Router, client-side routing) from a
/// dedicated wwwroot-partner folder under the "/partner" URL prefix — a
/// separate physical folder from the landing site's wwwroot and from
/// wwwroot-admin, synced by Scripts/build-partner.ps1. Any GET under
/// /partner that doesn't hit a real file falls back to /partner/index.html
/// so client-side routes (e.g. /partner/stations) resolve correctly on a
/// hard refresh or a direct link.
/// </summary>
internal sealed class PartnerSpaMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    private const string BasePath = "/partner";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? "";

        if (HttpMethods.IsGet(httpContext.Request.Method)
            && path.StartsWith(BasePath, StringComparison.OrdinalIgnoreCase)
            && !Path.HasExtension(path))
        {
            var partnerRoot = Path.Combine(environment.ContentRootPath, "wwwroot-partner");
            var relative = path[BasePath.Length..].TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physicalFile = Path.Combine(partnerRoot, relative);

            if (!File.Exists(physicalFile))
            {
                var indexFile = Path.Combine(partnerRoot, "index.html");
                if (File.Exists(indexFile))
                    httpContext.Request.Path = $"{BasePath}/index.html";
            }
        }

        await next(httpContext);
    }
}

public static class PartnerSpaMiddlewareExtension
{
    /// <summary>
    /// Serves the Cable Partner SPA (built from ../../cable-partner with
    /// BASE_URL=/partner/, synced into wwwroot-partner by
    /// Scripts/build-partner.ps1) at /partner: SPA-fallback rewrite + static
    /// files with cache policy. Call BEFORE UseRouting, alongside
    /// UseLandingPage and UseAdminSpa. No-ops if wwwroot-partner hasn't been
    /// built yet (e.g. a fresh dev checkout), so it never breaks startup.
    /// </summary>
    public static IApplicationBuilder UsePartnerSpa(this IApplicationBuilder app)
    {
        var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
        var partnerRoot = Path.Combine(env.ContentRootPath, "wwwroot-partner");

        if (!Directory.Exists(partnerRoot))
            return app;

        app.UseMiddleware<PartnerSpaMiddleware>();

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(partnerRoot),
            RequestPath = "/partner",
            OnPrepareResponse = ctx =>
            {
                // index.html and the runtime config must always revalidate:
                // index.html so SPA updates are picked up on the next load, and
                // config.*.js because it is NOT fingerprinted — caching it for a
                // year would pin the API URL of whichever environment was
                // deployed first. Fingerprinted build assets cache forever.
                var name = ctx.File.Name;
                var mustRevalidate =
                    name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                    || (name.StartsWith("config.", StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(".js", StringComparison.OrdinalIgnoreCase));

                ctx.Context.Response.Headers.CacheControl = mustRevalidate
                    ? "no-cache"
                    : "public, max-age=31536000, immutable";
            }
        });

        return app;
    }
}
