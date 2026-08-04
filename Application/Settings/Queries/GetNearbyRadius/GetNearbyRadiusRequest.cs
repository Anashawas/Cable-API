using Application.Common.Interfaces;

namespace Application.Settings.Queries.GetNearbyRadius;

public record NearbyRadiusDto(double RadiusKm);

/// <summary>The global nearby-radius cap used by home-screen serving (public read).</summary>
public record GetNearbyRadiusRequest : IRequest<NearbyRadiusDto>;

public class GetNearbyRadiusRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetNearbyRadiusRequest, NearbyRadiusDto>
{
    public async Task<NearbyRadiusDto> Handle(GetNearbyRadiusRequest request, CancellationToken cancellationToken)
        => new(await AppSettingsProvider.GetNearbyRadiusKmAsync(applicationDbContext, cancellationToken));
}
