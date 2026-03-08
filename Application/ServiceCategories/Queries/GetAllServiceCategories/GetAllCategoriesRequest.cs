using Microsoft.EntityFrameworkCore;

namespace Application.ServiceCategories.Queries.GetAllServiceCategories;

public record ServiceCategoryDto(
    int Id,
    string Name,
    string? NameAr,
    string? Description,
    string? IconUrl,
    int SortOrder,
    bool IsActive
);

public record GetAllCategoriesRequest() : IRequest<List<ServiceCategoryDto>>;

public class GetAllCategoriesRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllCategoriesRequest, List<ServiceCategoryDto>>
{
    public async Task<List<ServiceCategoryDto>> Handle(GetAllCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        return await applicationDbContext.ServiceCategories
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new ServiceCategoryDto(
                x.Id,
                x.Name,
                x.NameAr,
                x.Description,
                x.IconUrl,
                x.SortOrder,
                x.IsActive
            ))
            .ToListAsync(cancellationToken);
    }
}
