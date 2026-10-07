using Microsoft.Extensions.FileProviders;

namespace Cable.Middlewares;

/// <summary>
/// Serves the Cable Admin SPA (React Router, client-side routing) from a
/// dedicated wwwroot-admin folder under the "/admin" URL prefix — a
/// separate physical folder from the landing site's wwwroot, synced by
/// Scripts/build-admin.ps1. Unlike the landing site's static export (one
/// physical index.html per route), any GET under /admin that doesn't hit a
/// real file falls back to /admin/index.html so client-side routes (e.g.
/// /admin/users) resolve correctly on a hard refresh or direct link.
/// </summary>
internal sealed class AdminSpaMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    private const string BasePath = "/admin";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? "";

        if (HttpMethods.IsGet(httpContext.Request.Method)
            && path.StartsWith(BasePath, StringComparison.OrdinalIgnoreCase)
            && !Path.HasExtension(path))
        {
            var adminRoot = Path.Combine(environment.ContentRootPath, "wwwroot-admin");
            var relative = path[BasePath.Length..].TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physicalFile = Path.Combine(adminRoot, relative);

            if (!File.Exists(physicalFile))
            {
                var indexFile = Path.Combine(adminRoot, "index.html");
                if (File.Exists(indexFile))
                    httpContext.Request.Path = $"{BasePath}/index.html";
            }
        }

        await next(httpContext);
    }
}

public static class AdminSpaMiddlewareExtension
{
    /// <summary>
    /// Serves the Cable Admin SPA (built from ../Cable-Admin with
    /// BASE_URL=/admin/, synced into wwwroot-admin by Scripts/build-admin.ps1)
    /// at /admin: SPA-fallback rewrite + static files with cache policy.
    /// Call BEFORE UseRouting, alongside UseLandingPage. No-ops if
    /// wwwroot-admin hasn't been built yet (e.g. a fresh dev checkout),
    /// so it never breaks startup.
    /// </summary>
    public static IApplicationBuilder UseAdminSpa(this IApplicationBuilder app)
    {
        var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
        var adminRoot = Path.Combine(env.ContentRootPath, "wwwroot-admin");

        if (!Directory.Exists(adminRoot))
            return app;

        app.UseMiddleware<AdminSpaMiddleware>();

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(adminRoot),
            RequestPath = "/admin",
            OnPrepareResponse = ctx =>
            {
                // index.html must always revalidate so SPA updates are picked
                // up on the next load; fingerprinted build assets cache forever.
                if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                    ctx.Context.Response.Headers.CacheControl = "no-cache";
                else
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            }
        });

        return app;
    }
}
