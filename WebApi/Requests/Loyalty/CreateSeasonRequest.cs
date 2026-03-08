namespace Cable.Requests.Loyalty;

public record CreateSeasonRequest(
    string Name,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool ActivateImmediately
);
