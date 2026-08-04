using Cable.Core.Emuns;
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
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetAllCategoriesRequest, List<ServiceCategoryDto>>
{
    public async Task<List<ServiceCategoryDto>> Handle(GetAllCategoriesRequest request,
        CancellationToken cancellationToken)
    {
        var categories = await applicationDbContext.ServiceCategories
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);

        return categories.Select(x => new ServiceCategoryDto(
            x.Id,
            x.Name,
            x.NameAr,
            x.Description,
            !string.IsNullOrEmpty(x.IconUrl)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableServiceProvider, x.IconUrl)
                : null,
            x.SortOrder,
            x.IsActive
        )).ToList();
    }
}
