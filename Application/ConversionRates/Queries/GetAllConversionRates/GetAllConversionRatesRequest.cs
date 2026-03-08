using Microsoft.EntityFrameworkCore;

namespace Application.ConversionRates.Queries.GetAllConversionRates;

public record ConversionRateDto(
    int Id,
    string Name,
    string CurrencyCode,
    double PointsPerUnit,
    bool IsDefault,
    bool IsActive
);

public record GetAllConversionRatesRequest() : IRequest<List<ConversionRateDto>>;

public class GetAllConversionRatesRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetAllConversionRatesRequest, List<ConversionRateDto>>
{
    public async Task<List<ConversionRateDto>> Handle(GetAllConversionRatesRequest request,
        CancellationToken cancellationToken)
    {
        return await applicationDbContext.PointsConversionRates
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new ConversionRateDto(
                x.Id, x.Name, x.CurrencyCode,
                x.PointsPerUnit, x.IsDefault, x.IsActive
            ))
            .ToListAsync(cancellationToken);
    }
}
