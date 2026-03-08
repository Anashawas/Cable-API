namespace Cable.Requests.ServiceCategories;

public record CreateServiceCategoryRequest(
    string Name,
    string? NameAr,
    string? Description,
    string? IconUrl,
    int SortOrder,
    bool IsActive
);
