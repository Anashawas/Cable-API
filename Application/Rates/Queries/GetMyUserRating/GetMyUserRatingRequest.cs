using Application.Rates.Queries.GetUserRating;
using Cable.Core;

namespace Application.Rates.Queries.GetMyUserRating;

/// <summary>
/// The caller's own standing as a driver — their average, and every rating
/// providers have left about them. Returns an empty summary (average null,
/// count 0) for a driver nobody has rated yet, rather than 404.
/// </summary>
public record GetMyUserRatingRequest : IRequest<UserRatingSummaryDto>;

public class GetMyUserRatingRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyUserRatingRequest, UserRatingSummaryDto>
{
    public async Task<UserRatingSummaryDto> Handle(GetMyUserRatingRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        return await UserRatingReader.GetSummaryAsync(applicationDbContext, userId, cancellationToken);
    }
}
