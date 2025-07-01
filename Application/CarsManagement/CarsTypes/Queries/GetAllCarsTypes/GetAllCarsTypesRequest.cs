using Microsoft.EntityFrameworkCore;

namespace Application.CarsManagement.CarsTypes.Queries.GetAllCarsTypes;

public record GetAllCarsTypesRequest() : IRequest<List<GetAllCarsTypesDto>>;

public record GetAllCarsTypesDto(int Id, string Name);

public class GetAllCarsTypesQueriesHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllCarsTypesRequest, List<GetAllCarsTypesDto>>
{
    public async Task<List<GetAllCarsTypesDto>> Handle(GetAllCarsTypesRequest request,
        CancellationToken cancellationToken)
        =>
            await applicationDbContext.CarTypes.Select(x => new GetAllCarsTypesDto(x.Id, x.Name))
                .ToListAsync(cancellationToken: cancellationToken);
}