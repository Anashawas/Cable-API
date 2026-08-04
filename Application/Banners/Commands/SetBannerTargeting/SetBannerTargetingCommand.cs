using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Commands.SetBannerTargeting;

/// <summary>
/// Admin: set the ad-serving fields of a banner (targeting, priority, entity
/// link, campaign). A separate endpoint on purpose — the legacy
/// Add/UpdateBanner contracts stay untouched.
/// </summary>
public record SetBannerTargetingCommand(
    int Id,
    string TargetType = "national",
    string? TargetCity = null,
    double? CenterLat = null,
    double? CenterLng = null,
    double? RadiusKm = null,
    string? LinkedEntityType = null,
    int? LinkedEntityId = null,
    int? Priority = null,
    int? CampaignId = null
) : IRequest;

public class SetBannerTargetingCommandValidator : AbstractValidator<SetBannerTargetingCommand>
{
    public SetBannerTargetingCommandValidator()
    {
        RuleFor(x => x.TargetType).Must(t => t is "national" or "city" or "radius")
            .WithMessage("targetType must be national, city or radius");
        RuleFor(x => x.TargetCity).NotEmpty().When(x => x.TargetType == "city")
            .WithMessage("targetCity is required when targetType is city");
        RuleFor(x => x.CenterLat).NotNull().When(x => x.TargetType == "radius")
            .WithMessage("centerLat is required when targetType is radius");
        RuleFor(x => x.CenterLng).NotNull().When(x => x.TargetType == "radius")
            .WithMessage("centerLng is required when targetType is radius");
        RuleFor(x => x.RadiusKm).GreaterThan(0).When(x => x.RadiusKm.HasValue);
        RuleFor(x => x.LinkedEntityType).Must(t => t is null or "station" or "shop")
            .WithMessage("linkedEntityType must be station or shop");
    }
}

public class SetBannerTargetingCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SetBannerTargetingCommand>
{
    public async Task Handle(SetBannerTargetingCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var banner = await applicationDbContext.Banners
                         .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"Can not find banner with id {request.Id}");

        if (request.CampaignId.HasValue
            && !await applicationDbContext.Campaigns.AnyAsync(c => c.Id == request.CampaignId.Value && !c.IsDeleted, cancellationToken))
            throw new DataValidationException("CampaignId", $"Campaign {request.CampaignId} does not exist");

        banner.TargetType = request.TargetType;
        banner.TargetCity = request.TargetType == "city" ? request.TargetCity : null;
        banner.CenterLat = request.TargetType == "radius" ? request.CenterLat : null;
        banner.CenterLng = request.TargetType == "radius" ? request.CenterLng : null;
        banner.RadiusKm = request.TargetType == "radius" ? request.RadiusKm : null;
        banner.LinkedEntityType = request.LinkedEntityType;
        banner.LinkedEntityId = request.LinkedEntityType is null ? null : request.LinkedEntityId;
        banner.Priority = request.Priority;
        banner.CampaignId = request.CampaignId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
