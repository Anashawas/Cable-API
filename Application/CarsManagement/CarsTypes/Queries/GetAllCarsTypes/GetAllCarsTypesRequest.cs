using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsTypes.Queries.GetAllCarsTypes;

public record GetAllCarsTypesRequest() : IRequest<List<GetAllCarsTypesDto>>;

public record GetAllCarsTypesDto(int Id, string Name, string? IconUrl = null);

public class GetAllCarsTypesQueriesHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetAllCarsTypesRequest, List<GetAllCarsTypesDto>>
{
    public async Task<List<GetAllCarsTypesDto>> Handle(GetAllCarsTypesRequest request,
        CancellationToken cancellationToken)
    {
        var carTypes = await applicationDbContext.CarTypes
            .AsNoTracking()
            .Select(x => new { x.Id, x.Name, x.Icon })
            .ToListAsync(cancellationToken: cancellationToken);

        return carTypes
            .Select(x => new GetAllCarsTypesDto(
                x.Id,
                x.Name,
                !string.IsNullOrEmpty(x.Icon)
                    ? uploadFileService.GetFilePath(UploadFileFolders.CableCarTypes, x.Icon)
                    : null))
            .ToList();
    }
}
