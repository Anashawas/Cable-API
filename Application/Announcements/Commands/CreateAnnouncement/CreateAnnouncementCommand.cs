using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Cable.Core.Utilities;

namespace Application.Announcements.Commands.CreateAnnouncement;

/// <summary>Admin: create a welcome-message takeover (admin-only by design — partners cannot).</summary>
public record CreateAnnouncementCommand(
    string TitleEn,
    string TitleAr,
    string BodyEn,
    string BodyAr,
    string? ImageUrl,
    int? ActionType,
    string? ActionUrl,
    string? ActionLabelEn = null,
    string? ActionLabelAr = null,
    string TargetType = "national",
    string? TargetCity = null,
    double? CenterLat = null,
    double? CenterLng = null,
    double? RadiusKm = null,
    string Audience = "all",
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    int MaxPerDay = 1,
    int CooldownHours = 24,
    int MaxLifetime = 3,
    bool StopOnDismiss = true,
    int? CampaignId = null,
    int? AdvertiserId = null
) : IRequest<int>;

public class CreateAnnouncementCommandValidator : AbstractValidator<CreateAnnouncementCommand>
{
    public CreateAnnouncementCommandValidator()
    {
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BodyEn).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.BodyAr).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.TargetType).Must(t => t is "national" or "city" or "radius")
            .WithMessage("targetType must be national, city or radius");
        RuleFor(x => x.Audience).Must(a => a is "all" or "guests" or "loggedIn")
            .WithMessage("audience must be all, guests or loggedIn");
        RuleFor(x => x.TargetCity).NotEmpty().When(x => x.TargetType == "city")
            .WithMessage("targetCity is required when targetType is city");
        RuleFor(x => x.CenterLat).NotNull().When(x => x.TargetType == "radius");
        RuleFor(x => x.CenterLng).NotNull().When(x => x.TargetType == "radius");
        RuleFor(x => x.ActionType).InclusiveBetween(1, 5).When(x => x.ActionType.HasValue);
        RuleFor(x => x.MaxPerDay).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CooldownHours).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MaxLifetime).GreaterThanOrEqualTo(0);
    }
}

public class CreateAnnouncementCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateAnnouncementCommand, int>
{
    public async Task<int> Handle(CreateAnnouncementCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        if (request.CampaignId.HasValue
            && !await applicationDbContext.Campaigns.AnyAsync(c => c.Id == request.CampaignId.Value && !c.IsDeleted, cancellationToken))
            throw new DataValidationException("CampaignId", $"Campaign {request.CampaignId} does not exist");

        if (request.AdvertiserId.HasValue
            && !await applicationDbContext.Advertisers.AnyAsync(a => a.Id == request.AdvertiserId.Value && !a.IsDeleted, cancellationToken))
            throw new DataValidationException("AdvertiserId", $"Advertiser {request.AdvertiserId} does not exist");

        var announcement = new Domain.Enitites.Announcement
        {
            TitleEn = request.TitleEn.Trim(),
            TitleAr = request.TitleAr.Trim(),
            BodyEn = request.BodyEn.Trim(),
            BodyAr = request.BodyAr.Trim(),
            ImageUrl = request.ImageUrl,
            ActionType = request.ActionType,
            ActionUrl = request.ActionUrl,
            ActionLabelEn = request.ActionLabelEn,
            ActionLabelAr = request.ActionLabelAr,
            TargetType = request.TargetType,
            TargetCity = request.TargetCity,
            CenterLat = request.CenterLat,
            CenterLng = request.CenterLng,
            RadiusKm = request.RadiusKm,
            Audience = request.Audience,
            StartDate = JordanTime.ToUtc(request.StartDate) ?? DateTime.UtcNow,
            EndDate = JordanTime.ToUtc(request.EndDate),
            MaxPerDay = request.MaxPerDay,
            CooldownHours = request.CooldownHours,
            MaxLifetime = request.MaxLifetime,
            StopOnDismiss = request.StopOnDismiss,
            IsActive = true,
            CampaignId = request.CampaignId,
            AdvertiserId = request.AdvertiserId
        };

        applicationDbContext.Announcements.Add(announcement);
        await applicationDbContext.SaveChanges(cancellationToken);
        return announcement.Id;
    }
}
