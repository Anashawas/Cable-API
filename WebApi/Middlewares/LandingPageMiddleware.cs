namespace Cable.Middlewares;

/// <summary>
/// Maps the landing site's static-export URL shape onto wwwroot (the Next.js
/// export stores pages as trailing-slash folders, e.g. ar/index.html):
/// - "/" and "/ar/" are rewritten to their folder's index.html so the
///   static-files middleware can serve them
/// - bare "/ar" is 301-redirected to "/ar/" so hand-typed URLs work
/// - deep links /c/{code}, /r/{receiptNo} (and /ar|en/c|r/…) are rewritten to
///   that folder's index.html, which reads the code from the URL
///   (GET only, only when the page exists)
/// Only acts when a matching folder with index.html exists under wwwroot,
/// so API routes are never touched (there is no wwwroot/api folder).
/// </summary>
internal sealed class LandingPageMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value ?? "";
        var webRoot = environment.WebRootPath;

        if (webRoot is not null && HttpMethods.IsGet(httpContext.Request.Method) && !Path.HasExtension(path))
        {
            if (DeepLinkPage(path) is { } page
                && File.Exists(Path.Combine(webRoot, page.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))))
            {
                // Sticker / receipt links: one static page per kind reads the code from the URL client-side.
                httpContext.Request.Path = page;
            }
            else if (path.EndsWith('/'))
            {
                var indexFile = Path.Combine(webRoot, path.Trim('/').Replace('/', Path.DirectorySeparatorChar), "index.html");
                if (File.Exists(indexFile))
                    httpContext.Request.Path = path + "index.html";
            }
            else if (path.Length > 1
                     && File.Exists(Path.Combine(webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar), "index.html")))
            {
                httpContext.Response.Redirect(path + "/" + httpContext.Request.QueryString, permanent: true);
                return;
            }
        }

        await next(httpContext);
    }

    /// <summary>
    /// The plug-sticker and receipt deep links (§10 / §13.2): /c/{code} and /r/{receiptNo} (optional trailing
    /// slash, optional /ar or /en prefix) → that folder's index.html, e.g. /ar/c/CHG-7K2M9Q → /ar/c/index.html.
    /// Null for anything else (including /c/ itself, which the folder rule serves).
    /// </summary>
    internal static string? DeepLinkPage(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var offset = segments.Length == 3 && (segments[0] is "ar" or "en") ? 1 : 0;
        if (segments.Length != 2 + offset) return null;

        var kind = segments[offset];
        if (kind is not ("c" or "r") || segments[offset + 1].Length is 0 or > 64) return null;

        return (offset == 1 ? "/" + segments[0] : "") + "/" + kind + "/index.html";
    }
}

public static class LandingPageMiddlewareExtension
{
    /// <summary>
    /// Serves the landing site (static export synced into wwwroot by
    /// Scripts/build-landing.ps1) at the domain root: URL-shape middleware +
    /// static files with cache policy. Call BEFORE UseRouting so static pages
    /// are served before endpoint matching claims the request.
    /// </summary>
    public static IApplicationBuilder UseLandingPage(this IApplicationBuilder app)
    {
        app.UseMiddleware<LandingPageMiddleware>();

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                // Next.js fingerprints everything under _next/static — cache forever.
                if (ctx.Context.Request.Path.StartsWithSegments("/_next/static"))
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                else if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    ctx.Context.Response.Headers.CacheControl = "no-cache";
            }
        });

        return app;
    }

    /// <summary>
    /// Unknown non-API URLs get the landing's designed 404 page; API-ish paths
    /// (/api, /OpenApi, /Cable-API, /Cable-Jobs-Dashboard) keep their plain
    /// 404s. Call after the route registrations, before app.Run().
    /// </summary>
    public static WebApplication MapLandingPageFallback(this WebApplication app)
    {
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            var notFoundPage = app.Environment.WebRootPath is { } webRoot
                ? Path.Combine(webRoot, "404.html")
                : null;

            if (HttpMethods.IsGet(context.Request.Method)
                && !path.StartsWithSegments("/api")
                && !path.StartsWithSegments("/OpenApi")
                && !path.StartsWithSegments("/Cable-API")
                && !path.StartsWithSegments("/Cable-Jobs-Dashboard")
                && !path.StartsWithSegments("/admin")
                && notFoundPage is not null && File.Exists(notFoundPage))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(notFoundPage);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
        });

        return app;
    }
}
