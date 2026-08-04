namespace Application.SocialMediaPlatforms.Queries.GetAllSocialMediaPlatforms;

public record SocialMediaPlatformDto(
    int     Id,
    string  Name,
    string? NameAr,
    string? IconUrl,
    int     DisplayOrder,
    bool    IsActive
);
