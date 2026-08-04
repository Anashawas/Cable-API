using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Queries.GetChargingPointReviews;

public record ChargingPointReviewDto(
    int Id,
    int UserId,
    string? UserName,
    int Rating,
    string? Comment,
    DateTime CreatedAt);

public record GetChargingPointReviewsDto(
    int ChargingPointId,
    double AverageRating,
    int TotalReviews,
    List<ChargingPointReviewDto> Reviews);

/// <summary>Ratings with their review comments for a charging point, newest first.</summary>
public record GetChargingPointReviewsRequest(int ChargingPointId) : IRequest<GetChargingPointReviewsDto>;

public class GetChargingPointReviewsRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetChargingPointReviewsRequest, GetChargingPointReviewsDto>
{
    public async Task<GetChargingPointReviewsDto> Handle(GetChargingPointReviewsRequest request,
        CancellationToken cancellationToken)
    {
        var exists = await applicationDbContext.ChargingPoints
            .AnyAsync(x => x.Id == request.ChargingPointId && !x.IsDeleted, cancellationToken);
        if (!exists)
            throw new NotFoundException($"can not find charging point with id {request.ChargingPointId}");

        var reviews = await applicationDbContext.Rates
            .AsNoTracking()
            .Where(x => x.ChargingPointId == request.ChargingPointId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ChargingPointReviewDto(
                x.Id,
                x.UserId,
                x.User.Name,
                x.ChargingPointRate,
                x.Comment,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return new GetChargingPointReviewsDto(
            request.ChargingPointId,
            reviews.Any() ? Math.Round(reviews.Average(r => r.Rating), 2) : 0,
            reviews.Count,
            reviews);
    }
}
