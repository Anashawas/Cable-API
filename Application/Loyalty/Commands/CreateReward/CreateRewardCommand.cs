using Cable.Core;
using FluentValidation;

namespace Application.Loyalty.Commands.CreateReward;

public record CreateRewardCommand(
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
    DateTime ValidFrom,
    DateTime? ValidTo
) : IRequest<int>;

public class CreateRewardCommandValidator : AbstractValidator<CreateRewardCommand>
{
    public CreateRewardCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.PointsCost).GreaterThan(0);
        RuleFor(x => x.RewardType).InclusiveBetween(1, 5);
        RuleFor(x => x.ProviderType).MaximumLength(50)
            .Must(x => x is null or "ChargingPoint" or "ServiceProvider")
            .WithMessage("ProviderType must be 'ChargingPoint', 'ServiceProvider', or null");
        RuleFor(x => x.ValidFrom).NotEmpty();
        RuleFor(x => x.ValidTo).GreaterThan(x => x.ValidFrom).When(x => x.ValidTo.HasValue);
    }
}

public class CreateRewardCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateRewardCommand, int>
{
    public async Task<int> Handle(CreateRewardCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var now = DateTime.UtcNow;
        var reward = new LoyaltyReward
        {
            Name = request.Name,
            Description = request.Description,
            PointsCost = request.PointsCost,
            RewardType = request.RewardType,
            RewardValue = request.RewardValue,
            ProviderType = request.ProviderType,
            ProviderId = request.ProviderId,
            ServiceCategoryId = request.ServiceCategoryId,
            MaxRedemptions = request.MaxRedemptions,
            CurrentRedemptions = 0,
            ImageUrl = request.ImageUrl,
            IsActive = true,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            
        };

        applicationDbContext.LoyaltyRewards.Add(reward);
        await applicationDbContext.SaveChanges(cancellationToken);

        return reward.Id;
    }
}
