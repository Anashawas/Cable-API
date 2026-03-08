using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Queries.GetServiceProviderRatings;

public record ServiceProviderRatingDto(
    int Id,
    int UserId,
    string? UserName,
    int Rating,
    double AVGRating,
    string? Comment,
    DateTime CreatedAt
);

public record GetProviderRatingsRequest(int ServiceProviderId) : IRequest<List<ServiceProviderRatingDto>>;

public class GetProviderRatingsRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetProviderRatingsRequest, List<ServiceProviderRatingDto>>
{
    public async Task<List<ServiceProviderRatingDto>> Handle(GetProviderRatingsRequest request,
        CancellationToken cancellationToken)
    {
        return await applicationDbContext.ServiceProviderRates
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.ServiceProviderId == request.ServiceProviderId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ServiceProviderRatingDto(
                x.Id,
                x.UserId,
                x.User.Name,
                x.Rating,
                x.AVGRating,
                x.Comment,
                x.CreatedAt
            ))
            .ToListAsync(cancellationToken);
    }
}
