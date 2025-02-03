namespace Cable.Requests.Banners;

public record UpdateBannerRequest(string Name,
    string Phone,
    string Email,
    DateOnly? StartDate,
    DateOnly? EndDate);

