using Cable.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Application.SharedLinks.Commands.CreateSharedLink;

public record CreateSharedLinkCommand(
    string LinkType,
    int? TargetId,
    string? Parameters,
    DateTime? ExpiresAt,
    int MaxUsage = 1
) : IRequest<string>;

public class CreateSharedLinkCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IHttpContextAccessor httpContextAccessor)
    : IRequestHandler<CreateSharedLinkCommand, string>
{
    public async Task<string> Handle(CreateSharedLinkCommand request, CancellationToken cancellationToken)
    {
        var linkType = await applicationDbContext.SharedLinkTypes.AsNoTracking()
                           .FirstOrDefaultAsync(x => x.TypeName == request.LinkType && x.IsActive, cancellationToken)
                       ?? throw new NotFoundException($"Link type '{request.LinkType}' not found or inactive");

        var linkToken = GenerateUniqueToken();
        
        var sharedLink = new SharedLink
        {
            LinkToken = linkToken,
            LinkType = request.LinkType,
            TargetId = request.TargetId,
            Parameters = request.Parameters,
            ExpiresAt = request.ExpiresAt,
            MaxUsage = request.MaxUsage,
            CurrentUsage = 0,
            IsActive = true,
        };

        applicationDbContext.SharedLinks.Add(sharedLink);
        await applicationDbContext.SaveChanges(cancellationToken);

        var baseUrl = GetBaseUrl();
        var language = GetUserLanguage();
        var sharedUrl = $"{baseUrl}/shared/{linkToken}";
        
        // Add language parameter if detected
        if (!string.IsNullOrEmpty(language))
        {
            sharedUrl += $"?lang={language}";
        }
        
        return sharedUrl;
    }

    private string GetBaseUrl()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null)
            return "https://localhost:7272"; // Default fallback
        
        var request = httpContext.Request;
        return $"{request.Scheme}://{request.Host}";
    }

    private string GetUserLanguage()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext == null) return "ar"; // Default to Arabic
        
        // Get Accept-Language header
        var acceptLanguage = httpContext.Request.Headers["Accept-Language"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(acceptLanguage))
            return "ar"; // Default to Arabic
        
        // Parse Accept-Language header (e.g., "en-US,en;q=0.9,ar;q=0.8")
        var languages = acceptLanguage.Split(',')
            .Select(lang => lang.Split(';')[0].Trim().ToLower())
            .ToList();
        
        // Check for Arabic variants first
        if (languages.Any(lang => lang.StartsWith("ar")))
            return "ar";
        
        // Check for English variants
        if (languages.Any(lang => lang.StartsWith("en")))
            return "en";
        
        // Default to Arabic for Middle Eastern market
        return "ar";
    }

    private static string GenerateUniqueToken()
    {
        return Guid.NewGuid().ToString("N")[..16] + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    }
}