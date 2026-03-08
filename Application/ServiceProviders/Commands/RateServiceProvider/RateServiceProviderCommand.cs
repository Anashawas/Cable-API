using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ServiceProviders.Commands.RateServiceProvider;

public record RateServiceProviderCommand(int ServiceProviderId, int Rating, string? Comment) : IRequest<int>;

public class RateServiceProviderCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    ILoyaltyPointService loyaltyPointService)
    : IRequestHandler<RateServiceProviderCommand, int>
{
    public async Task<int> Handle(RateServiceProviderCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var serviceProvider = await applicationDbContext.ServiceProviders
                                  .FirstOrDefaultAsync(x => x.Id == request.ServiceProviderId && !x.IsDeleted, cancellationToken)
                              ?? throw new NotFoundException($"Service provider with id {request.ServiceProviderId} not found");

        // Calculate new average
        var existingRates = await applicationDbContext.ServiceProviderRates
            .Where(x => x.ServiceProviderId == request.ServiceProviderId && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        var totalRatings = existingRates.Count + 1;
        var sumRatings = existingRates.Sum(x => x.Rating) + request.Rating;
        var avgRating = (double)sumRatings / totalRatings;

        var rate = new ServiceProviderRate
        {
            ServiceProviderId = request.ServiceProviderId,
            UserId = userId,
            Rating = request.Rating,
            AVGRating = avgRating,
            Comment = request.Comment
        };

        applicationDbContext.ServiceProviderRates.Add(rate);
        await applicationDbContext.SaveChanges(cancellationToken);

        // Award loyalty points
        await loyaltyPointService.AwardPointsAsync(userId, "RATE_SERVICE", "ServiceProvider", request.ServiceProviderId, cancellationToken: cancellationToken);

        return rate.Id;
    }
}
