using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsModels.Queries.GetAllCarsModels;

public record GetAllCarsModelsRequest() : IRequest<List<GetAllCarsModelsDto>>;

public record GetAllCarsModelsDto(int Id, string Name, List<CarModelSummary> CarModels, string? IconUrl = null);

public record CarModelSummary(int Id, string Name, int? SizeId = null, string? SizeName = null);

public class GetAllCarsModelsRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetAllCarsModelsRequest, List<GetAllCarsModelsDto>>
{
    public async Task<List<GetAllCarsModelsDto>> Handle(GetAllCarsModelsRequest request,
        CancellationToken cancellationToken)
    {
        var carTypes = await applicationDbContext.CarTypes
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Icon,
                Models = x.CarModels.Select(m => new
                {
                    m.Id,
                    m.Name,
                    m.SizeId,
                    SizeName = m.Size != null ? m.Size.Name : null
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        return carTypes
            .Select(x => new GetAllCarsModelsDto(
                x.Id,
                x.Name,
                x.Models.Select(m => new CarModelSummary(m.Id, m.Name, m.SizeId, m.SizeName)).ToList(),
                !string.IsNullOrEmpty(x.Icon)
                    ? uploadFileService.GetFilePath(UploadFileFolders.CableCarTypes, x.Icon)
                    : null))
            .ToList();
    }
}
