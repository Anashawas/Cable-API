using Application.Common.Interfaces;
using Application.Common.Models;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Announcements.Queries.GetAllAnnouncements;

public record AnnouncementAdminDto(
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
    int? AdvertiserId,
    string? AdvertiserName,
    DateTime CreatedAt);

/// <summary>Admin: all welcome messages (active and not).</summary>
public record GetAllAnnouncementsRequest(int? Page = null, int? PageSize = null)
    : IRequest<PagedResult<AnnouncementAdminDto>>;

public class GetAllAnnouncementsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAllAnnouncementsRequest, PagedResult<AnnouncementAdminDto>>
{
    public async Task<PagedResult<AnnouncementAdminDto>> Handle(GetAllAnnouncementsRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        return await applicationDbContext.Announcements.AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.IsActive)
            .ThenByDescending(a => a.Id)
            .Select(a => new AnnouncementAdminDto(
                a.Id, a.TitleEn, a.TitleAr, a.BodyEn, a.BodyAr, a.ImageUrl,
                a.ActionType, a.ActionUrl, a.ActionLabelEn, a.ActionLabelAr,
                a.TargetType, a.TargetCity, a.CenterLat, a.CenterLng, a.RadiusKm, a.Audience,
                a.StartDate, a.EndDate,
                a.MaxPerDay, a.CooldownHours, a.MaxLifetime, a.StopOnDismiss, a.IsActive,
                a.CampaignId, a.AdvertiserId,
                a.Advertiser != null ? a.Advertiser.Name : null,
                a.CreatedAt))
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
    }
}
