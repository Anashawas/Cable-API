using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.UpdateReward;

public record UpdateRewardCommand(
    int Id,
    string Name,
    string? Description,
    int PointsCost,
    int RewardType,
    string? RewardValue,
    string? ProviderType,
    int? ProviderId,
    int? ServiceCategoryId,
    int? MaxRedemptions,
    string? ImageUrl,
    bool IsActive,
    DateTime ValidFrom,
    DateTime? ValidTo
) : IRequest;

public class UpdateRewardCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateRewardCommand>
{
    public async Task Handle(UpdateRewardCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var reward = await applicationDbContext.LoyaltyRewards
                         .FirstOrDefaultAsync(r => r.Id == request.Id && !r.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"Reward with Id '{request.Id}' not found");

        var now = DateTime.UtcNow;
        reward.Name = request.Name;
        reward.Description = request.Description;
        reward.PointsCost = request.PointsCost;
        reward.RewardType = request.RewardType;
        reward.RewardValue = request.RewardValue;
        reward.ProviderType = request.ProviderType;
        reward.ProviderId = request.ProviderId;
        reward.ServiceCategoryId = request.ServiceCategoryId;
        reward.MaxRedemptions = request.MaxRedemptions;
        reward.ImageUrl = request.ImageUrl;
        reward.IsActive = request.IsActive;
        reward.ValidFrom = request.ValidFrom;
        reward.ValidTo = request.ValidTo;
        reward.ModifiedAt = now;
        reward.ModifiedBy = userId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
