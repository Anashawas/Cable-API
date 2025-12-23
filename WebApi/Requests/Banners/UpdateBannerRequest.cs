namespace Cable.Requests.Banners;

public record UpdateBannerRequest(string Name,
    string Phone,
    string Email,
    int? ActionType,
    string? ActionUrl,
    DateOnly? StartDate,
    DateOnly? EndDate);

