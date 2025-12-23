
using Cable.Core.Exceptions;
using System.Security.Claims;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Extenstions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using static System.Enum;

namespace Cable.WebApi.Middlewares;

internal sealed class SecureFileServingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string[] _securedFolders;
    private readonly string[] _allowedFolders;
    private readonly IHttpContextAccessor _contextAccessor;
    private readonly IConfiguration _configuration;
    private readonly string _fileUploadPath;
    private readonly string _sharedLinkPath;
    
    public SecureFileServingMiddleware(RequestDelegate next, IHttpContextAccessor contextAccessor, IConfiguration configuration)
    {
        _next = next;
        _contextAccessor = contextAccessor;
        _configuration = configuration;
        _fileUploadPath = _configuration.GetValue<string>("File:FileUploadPath") 
            ?? throw new NotFoundException("File:FileUploadPath configuration is required");
        _securedFolders = GetNames<UploadFileFolders>().Select(f => f.ToLower()).ToArray();
        _allowedFolders = GetNames<AllowedUploadFiles>().Select(f => f.ToLower()).ToArray();
        _sharedLinkPath = _configuration.GetValue<string>("SharedLink:PagePath") ;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var path = httpContext.Request.Path.Value?.ToLower();
        
        if (IsFileRequest(path))
        {
            await HandleFileRequest(httpContext, path);
            return;
        }
        
        if (IsSharedLink(path))
        {
            await HandleSharedLinkRequest(httpContext, path);
            return;
        }

        await _next(httpContext);
    }
    
    private bool IsFileRequest(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
     
        return _securedFolders.Any(folder => 
            path.StartsWith($"/{folder}/") || 
            path.Equals($"/{folder}", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSharedLink(string? path)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(_sharedLinkPath))
            return false;
        
        return path.StartsWith("/shared/", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/shared/index.html", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/Shared/", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/Shared/index.html", StringComparison.OrdinalIgnoreCase);
    }
    
    private async Task HandleSharedLinkRequest(HttpContext httpContext, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(_sharedLinkPath))
            {
                throw new NotFoundException("Shared link page not configured");
            }

            var fullPath = Path.GetFullPath(_sharedLinkPath);
            
            if (!File.Exists(fullPath))
            {
                throw new NotFoundException("Shared link page not found");
            }

            // Extract token from path for potential use in the HTML
            var pathParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var token = pathParts.Length >= 2 ? pathParts[1] : "";

            // Set content type to HTML
            httpContext.Response.ContentType = "text/html";

            // Read and serve the HTML file
            var htmlContent = await File.ReadAllTextAsync(fullPath);
            
            // Optionally inject the token into the HTML if needed
            if (!string.IsNullOrEmpty(token))
            {
                htmlContent = htmlContent.Replace("{{TOKEN}}", token);
            }

            await httpContext.Response.WriteAsync(htmlContent);
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CableApplicationException("Error serving shared link page");
        }
    }

    private async Task HandleFileRequest(HttpContext httpContext, string path)
    {
        try
        {
            var pathParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pathParts.Length < 2)
            {
                throw new NotFoundException("File not found");
            }

            var folder = pathParts[0].ToLower();
            var fileName = SanitizeFileName(string.Join("/",pathParts.Length ==3? pathParts.Skip(2):pathParts.Skip(1)));

            if (string.IsNullOrEmpty(fileName) || fileName.Contains(".."))
            {
                throw new NotFoundException("Invalid file path");
            }
            
            if (_allowedFolders.Contains(folder))
            {
                await ServePublicFile(httpContext, folder, fileName);
                return;
            }
            
            if (_securedFolders.Contains(folder))
            {
                await ServePrivateFile(httpContext, folder, fileName);
                return;
            }

            throw new NotFoundException("File not found");
        }
        catch (NotAuthorizedAccessException)
        {
            throw;
        }
        catch (NotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CableApplicationException("Error retrieving file");
        }
    }

    private async Task ServePublicFile(HttpContext httpContext, string folder, string fileName)
    {
        var physicalPath = Path.Combine(_fileUploadPath, folder, fileName);
        await ServeFile(httpContext, physicalPath, fileName);
    }

    private async Task ServePrivateFile(HttpContext httpContext, string folder, string fileName)
    {
        var userId = GetAuthenticatedUserId();
        var physicalPath = Path.Combine(_fileUploadPath,  folder, userId.ToString(), fileName);
        await ServeFile(httpContext, physicalPath, fileName);
    }

    private int GetAuthenticatedUserId()
    {
        var userIdClaim = _contextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdClaim, out var userId))
        {
            throw new NotAuthorizedAccessException();
        }
        return userId;
    }

    private async Task ServeFile(HttpContext httpContext, string physicalPath, string fileName)
    {
        var fullPath = Path.GetFullPath(physicalPath);
        var basePath = Path.GetFullPath(_fileUploadPath);
        
        if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotFoundException("File not found");
        }

        if (!File.Exists(fullPath))
        {
            throw new NotFoundException("File not found");
        }

        var contentType = GetContentType(fileName);
        httpContext.Response.ContentType = contentType;

        if (httpContext.Request.Method == "HEAD")
        {
            httpContext.Response.ContentLength = new FileInfo(fullPath).Length;
            return;
        }

        await httpContext.Response.SendFileAsync(fullPath);
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;
        
        fileName = fileName.Replace("..", "");
        
        var invalidChars = Path.GetInvalidFileNameChars().Concat(['\\']).ToArray();
        
        var pathSegments = fileName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var sanitizedSegments = pathSegments.Select(segment => 
            string.Concat(segment.Where(c => !invalidChars.Contains(c)))
        ).Where(segment => !string.IsNullOrEmpty(segment));
            
        return string.Join("/", sanitizedSegments);
    }

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}

public static class SecureFileServingMiddlewareExtension
{
    public static IApplicationBuilder UseSecureFileServing(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecureFileServingMiddleware>();
    }
}