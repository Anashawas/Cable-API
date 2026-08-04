namespace Application.SocialLinks.Queries.GetSocialLinksByProvider;

public record SocialLinkDto(
    int     Id,
    string  ProviderType,
    int     ProviderId,
    int     SocialMediaPlatformId,
    string  SocialMediaPlatformName,
    string? SocialMediaPlatformNameAr,
    string? SocialMediaPlatformIconUrl,
    string  Url,
    int     DisplayOrder
);
