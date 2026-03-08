namespace Cable.Requests.ServiceCategories;

public record UpdateServiceCategoryRequest(
    string Name,
    string? NameAr,
    string? Description,
    string? IconUrl,
    int SortOrder,
    bool IsActive
);
