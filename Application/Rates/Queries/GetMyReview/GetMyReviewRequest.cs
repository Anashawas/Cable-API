using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Queries.GetMyReview;

public record MyReviewDto(
    int Id,
    int ChargingPointId,
    int ChargingPointRate,
    string? Comment,
    DateTime CreatedAt,
    DateTime? ModifiedAt);

/// <summary>F2: the caller's own review of a station, or null if they haven't rated it.</summary>
public record GetMyReviewRequest(int ChargingPointId) : IRequest<MyReviewDto?>;

public class GetMyReviewRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyReviewRequest, MyReviewDto?>
{
    public async Task<MyReviewDto?> Handle(GetMyReviewRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        return await applicationDbContext.Rates.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ChargingPointId == request.ChargingPointId && x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MyReviewDto(
                x.Id, x.ChargingPointId, x.ChargingPointRate, x.Comment, x.CreatedAt, x.ModifiedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
