using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Announcements.Commands.UpdateAnnouncement;

/// <summary>Admin: update a welcome message (content, targeting, caps, active flag).</summary>
public record UpdateAnnouncementCommand(
    int Id,
    string TitleEn,
    string TitleAr,
    string BodyEn,
    string BodyAr,
    string? ImageUrl,
    int? ActionType,
    string? ActionUrl,
    string? ActionLabelEn,
    string? ActionLabelAr,
    string TargetType,
    string? TargetCity,
    double? CenterLat,
    double? CenterLng,
    double? RadiusKm,
    string Audience,
    DateTime StartDate,
    DateTime? EndDate,
    int MaxPerDay,
    int CooldownHours,
    int MaxLifetime,
    bool StopOnDismiss,
    bool IsActive,
    int? CampaignId,
    int? AdvertiserId
) : IRequest;

public class UpdateAnnouncementCommandValidator : AbstractValidator<UpdateAnnouncementCommand>
{
    public UpdateAnnouncementCommandValidator()
    {
        RuleFor(x => x.TitleEn).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BodyEn).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.BodyAr).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.TargetType).Must(t => t is "national" or "city" or "radius");
        RuleFor(x => x.Audience).Must(a => a is "all" or "guests" or "loggedIn");
        RuleFor(x => x.ActionType).InclusiveBetween(1, 5).When(x => x.ActionType.HasValue);
    }
}

public class UpdateAnnouncementCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateAnnouncementCommand>
{
    public async Task Handle(UpdateAnnouncementCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var announcement = await applicationDbContext.Announcements
                               .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsDeleted, cancellationToken)
                           ?? throw new NotFoundException($"can not find announcement with id {request.Id}");

        if (request.CampaignId.HasValue
            && !await applicationDbContext.Campaigns.AnyAsync(c => c.Id == request.CampaignId.Value && !c.IsDeleted, cancellationToken))
            throw new DataValidationException("CampaignId", $"Campaign {request.CampaignId} does not exist");

        announcement.TitleEn = request.TitleEn.Trim();
        announcement.TitleAr = request.TitleAr.Trim();
        announcement.BodyEn = request.BodyEn.Trim();
        announcement.BodyAr = request.BodyAr.Trim();
        announcement.ImageUrl = request.ImageUrl;
        announcement.ActionType = request.ActionType;
        announcement.ActionUrl = request.ActionUrl;
        announcement.ActionLabelEn = request.ActionLabelEn;
        announcement.ActionLabelAr = request.ActionLabelAr;
        announcement.TargetType = request.TargetType;
        announcement.TargetCity = request.TargetCity;
        announcement.CenterLat = request.CenterLat;
        announcement.CenterLng = request.CenterLng;
        announcement.RadiusKm = request.RadiusKm;
        announcement.Audience = request.Audience;
        announcement.StartDate = request.StartDate;
        announcement.EndDate = request.EndDate;
        announcement.MaxPerDay = request.MaxPerDay;
        announcement.CooldownHours = request.CooldownHours;
        announcement.MaxLifetime = request.MaxLifetime;
        announcement.StopOnDismiss = request.StopOnDismiss;
        announcement.IsActive = request.IsActive;
        announcement.CampaignId = request.CampaignId;
        announcement.AdvertiserId = request.AdvertiserId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
