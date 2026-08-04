using Application.Analytics;
using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Ads.Queries.GetCampaignStats;

public record CampaignDayStatDto(DateTime Day, int Impressions, int Clicks);
public record CampaignCityStatDto(string City, int Impressions, int Clicks);

public record CampaignStatsDto(
    int CampaignId,
    int Impressions,
    int Clicks,
    double Ctr,
    int Dismisses,
    double? DismissRate,
    int UniqueUsers,
    List<CampaignDayStatDto> ByDay,
    List<CampaignCityStatDto> ByCity);

/// <summary>Admin: delivery report for a campaign across all its linked assets (banners + announcements).</summary>
public record GetCampaignStatsRequest(int CampaignId, DateTime? From = null, DateTime? To = null)
    : IRequest<CampaignStatsDto>;

public class GetCampaignStatsRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCampaignStatsRequest, CampaignStatsDto>
{
    public async Task<CampaignStatsDto> Handle(GetCampaignStatsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        if (!await applicationDbContext.Campaigns.AnyAsync(c => c.Id == request.CampaignId && !c.IsDeleted, cancellationToken))
            throw new NotFoundException($"can not find campaign with id {request.CampaignId}");

        var from = request.From ?? DateTime.UtcNow.AddDays(-30);
        var to = request.To ?? DateTime.UtcNow;

        var bannerIds = await applicationDbContext.Banners.AsNoTracking()
            .Where(b => b.CampaignId == request.CampaignId && !b.IsDeleted)
            .Select(b => b.Id).ToListAsync(cancellationToken);
        var announcementIds = await applicationDbContext.Announcements.AsNoTracking()
            .Where(a => a.CampaignId == request.CampaignId && !a.IsDeleted)
            .Select(a => a.Id).ToListAsync(cancellationToken);

        // All raw events of the campaign's assets in the window (one query, filtered in SQL).
        var events = await applicationDbContext.AnalyticsEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to
                        && ((e.EntityType == AnalyticsCatalog.Banner && bannerIds.Contains(e.EntityId))
                            || (e.EntityType == AnalyticsCatalog.Announcement && announcementIds.Contains(e.EntityId))))
            .Select(e => new { e.EventType, e.OccurredAt, e.City, e.UserId, e.AnonymousId })
            .ToListAsync(cancellationToken);

        var impressions = events.Count(e => e.EventType == (int)AnalyticsEventType.BannerView);
        var clicks = events.Count(e => e.EventType == (int)AnalyticsEventType.BannerClick
                                       || e.EventType == (int)AnalyticsEventType.CtaConversion);
        var dismisses = events.Count(e => e.EventType == (int)AnalyticsEventType.Dismiss);
        var uniqueUsers = events
            .Select(e => e.UserId?.ToString() ?? e.AnonymousId)
            .Where(x => x is not null)
            .Distinct()
            .Count();

        var byDay = events
            .GroupBy(e => e.OccurredAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new CampaignDayStatDto(
                g.Key,
                g.Count(e => e.EventType == (int)AnalyticsEventType.BannerView),
                g.Count(e => e.EventType == (int)AnalyticsEventType.BannerClick)))
            .ToList();

        var byCity = events
            .Where(e => !string.IsNullOrEmpty(e.City))
            .GroupBy(e => e.City!)
            .OrderByDescending(g => g.Count())
            .Select(g => new CampaignCityStatDto(
                g.Key,
                g.Count(e => e.EventType == (int)AnalyticsEventType.BannerView),
                g.Count(e => e.EventType == (int)AnalyticsEventType.BannerClick)))
            .ToList();

        return new CampaignStatsDto(
            request.CampaignId,
            impressions,
            clicks,
            impressions > 0 ? Math.Round((double)clicks / impressions, 4) : 0,
            dismisses,
            impressions > 0 ? Math.Round((double)dismisses / impressions, 4) : null,
            uniqueUsers,
            byDay,
            byCity);
    }
}
