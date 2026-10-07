using Application.Banners.Queries.GetAllBanners;
using Application.Common;
using Application.Common.Interfaces;
using Application.Settings;
using Cable.Core.Emuns;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Queries.GetNearbyBanners;

public record NearbyBannerDto(
    int Id,
    string Name,
    string? Phone,
    int? ActionType,
    string? ActionUrl,
    string TargetType,
    string? TargetCity,
    double? CenterLat,
    double? CenterLng,
    double? RadiusKm,
    string? LinkedEntityType,
    int? LinkedEntityId,
    int? Priority,
    double? DistanceKm,
    int? CampaignId,
    DateTime? StartDate,
    DateTime? EndDate,
    List<BannerAttachmentSummery> BannerAttachments);

/// <summary>
/// Location-targeted, schedule-enforced, ranked banners for the home carousel.
/// Layered: radius-hit (nearest first) → same city → national. Within a layer,
/// pacing (less-shown boosted), optional paid priority and a small jitter keep
/// delivery fair. The app renders the returned order as-is.
/// </summary>
public record GetNearbyBannersRequest(double? Lat, double? Lng, string? City)
    : IRequest<List<NearbyBannerDto>>;

public class GetNearbyBannersRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetNearbyBannersRequest, List<NearbyBannerDto>>
{
    /// <summary>
    /// Ranking is EQUAL-SHARE: within a layer, whichever banner has the fewest
    /// views SO FAR TODAY is served first.
    ///
    /// Why not a weighted score. The old formula was
    /// 0.7 x relevance + 0.3 x pacing, where relevance fell back to recency
    /// whenever no banner carried a Priority — which is every banner in
    /// production. Recency never changes, so the order was frozen: the newest
    /// banner held slot 1 every day and the oldest sat last. Because the app
    /// renders the list as a carousel and fires a view per slot as it scrolls
    /// into visibility, position decays hard — slot 1 earned about 2.6x slot 4
    /// (measured 2026-09-21: 1126 / 788 / 563 / 425). Advertisers paying the
    /// same got materially different exposure based only on signup order.
    ///
    /// Sorting by today's deficit instead makes it self-correcting: the banner
    /// that is behind takes slot 1, earns views, and hands the slot to whoever
    /// is now behind. There is no timer — the order is recomputed on every
    /// request, and AnalyticsService writes the rollup inline when a view
    /// lands, so consecutive requests always see settled counts.
    ///
    /// Counting resets on the JORDAN calendar day, so "equal" means equal
    /// within a day rather than drifting over a rolling window.
    /// </summary>
    private const double JitterRange = 0.05;

    /// <summary>
    /// Breaks ties when several banners share a view count — which is every
    /// banner at midnight, and any banner that has just gone live. Proximity
    /// wins for radius ads; Priority then recency for the flat layers. It never
    /// outranks the deficit, so it cannot reintroduce the frozen ordering.
    /// </summary>
    private const double TieBreakWeight = 0.05;

    public async Task<List<NearbyBannerDto>> Handle(GetNearbyBannersRequest request,
        CancellationToken cancellationToken)
    {
        // Scheduling is enforced on the Jordan calendar day (ads are sold per local day).
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

        var banners = await applicationDbContext.Banners.AsNoTracking()
            .Include(x => x.BannerAttachments)
            .Include(x => x.BannerDurations)
            .Where(x => !x.IsDeleted && x.BannerDurations.Any(d =>
                d.StartDate <= today && d.EndDate >= today))
            .ToListAsync(cancellationToken);

        if (banners.Count == 0)
            return [];

        var adminRadius = await AppSettingsProvider.GetNearbyRadiusKmAsync(applicationDbContext, cancellationToken);

        // Equal-share input: views so far in THIS JORDAN DAY, per banner.
        //
        // Counted from the raw AnalyticsEvent rows, NOT from AnalyticsDailyRollup.
        // The rollup buckets by UTC calendar date, and a Jordan day runs from
        // 21:00 UTC to 21:00 UTC — so it straddles two rollup buckets and a
        // Jordan day simply cannot be expressed from them. An earlier attempt to
        // approximate it by shifting the bucket boundary three hours pulled the
        // whole previous UTC day into the window, which ranked the day's
        // MOST-viewed banner second (observed on production 2026-09-22:
        // 47 -> 55 -> 48 -> 51 -> 54 with 55 on 648 views, the highest).
        //
        // OccurredAt is a real timestamp, so the window is exact and the reset
        // lands on Jordan midnight as intended. It is covered by
        // IX_AnalyticsEvent_Entity_Event_OccurredAt (EntityType, EntityId,
        // EventType, OccurredAt) — a bounded index seek over one day's events
        // for a handful of banners (~2k rows at current volume).
        var ids = banners.Select(b => b.Id).ToList();
        var jordanDayStartUtc = today.ToDateTime(TimeOnly.MinValue).AddHours(-3);
        var shownCounts = await applicationDbContext.AnalyticsEvents.AsNoTracking()
            .Where(e => e.EntityType == "Banner"
                        && e.EventType == (int)AnalyticsEventType.BannerView
                        && ids.Contains(e.EntityId)
                        && e.OccurredAt >= jordanDayStartUtc)
            .GroupBy(e => e.EntityId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var city = request.City?.Trim();
        var layer1 = new List<(Banner Banner, double Distance, double EffectiveRadius)>();
        var layer2 = new List<Banner>();
        var layer3 = new List<Banner>();

        foreach (var banner in banners)
        {
            switch (banner.TargetType)
            {
                case "radius" when banner.CenterLat.HasValue && banner.CenterLng.HasValue:
                    if (request.Lat.HasValue && request.Lng.HasValue)
                    {
                        var distance = GeoDistance.Km(request.Lat.Value, request.Lng.Value,
                            banner.CenterLat.Value, banner.CenterLng.Value);
                        var effectiveRadius = banner.RadiusKm is > 0 ? banner.RadiusKm.Value : adminRadius;
                        if (distance <= effectiveRadius)
                            layer1.Add((banner, distance, effectiveRadius));
                    }
                    // out of range (or no user location): a radius-targeted ad is
                    // not shown elsewhere — the advertiser paid for that area.
                    break;

                case "city" when !string.IsNullOrEmpty(banner.TargetCity):
                    if (!string.IsNullOrEmpty(city)
                        && string.Equals(banner.TargetCity.Trim(), city, StringComparison.OrdinalIgnoreCase))
                        layer2.Add(banner);
                    break;

                default: // "national" and any legacy/unset value
                    layer3.Add(banner);
                    break;
            }
        }

        int ViewsToday(int bannerId) => shownCounts.GetValueOrDefault(bannerId, 0);

        double Jitter() => (Random.Shared.NextDouble() * 2 - 1) * JitterRange;

        // Deficit: 1 for the least-served banner in the layer, 0 for the most.
        // A layer whose banners are all level (midnight, or a single banner)
        // yields 1 for everyone and the tie-break decides.
        double Deficit(int bannerId, int minShown, int maxShown) =>
            maxShown <= minShown ? 1 : 1 - (double)(ViewsToday(bannerId) - minShown) / (maxShown - minShown);

        // Layer 1: still gated on the radius — a banner the user is outside of
        // never appears here, whatever it is owed. Among the banners that DO
        // cover this user, the one furthest behind today leads; proximity only
        // separates banners on equal counts.
        var min1 = layer1.Count == 0 ? 0 : layer1.Min(x => ViewsToday(x.Banner.Id));
        var max1 = layer1.Count == 0 ? 0 : layer1.Max(x => ViewsToday(x.Banner.Id));
        var ordered1 = layer1
            .OrderByDescending(x =>
                Deficit(x.Banner.Id, min1, max1)
                + TieBreakWeight * Math.Clamp(1 - x.Distance / x.EffectiveRadius, 0, 1)
                + Jitter())
            .Select(x => ToDto(x.Banner, x.Distance))
            .ToList();

        List<NearbyBannerDto> RankFlatLayer(List<Banner> layer)
        {
            if (layer.Count == 0) return [];

            var minShown = layer.Min(b => ViewsToday(b.Id));
            var maxShown = layer.Max(b => ViewsToday(b.Id));
            var maxPriority = layer.Max(b => b.Priority ?? 0);
            var byRecency = layer.OrderByDescending(b => b.CreatedAt).ToList();

            return layer
                .OrderByDescending(b =>
                {
                    // Priority and recency are tie-breaks only. Scaled by
                    // TieBreakWeight they cannot outweigh a real deficit, which
                    // is the whole point: recency used to dominate and froze the
                    // order for the life of the banner.
                    var priorityOrRecency = maxPriority > 0
                        ? (double)(b.Priority ?? 0) / maxPriority
                        : 1 - (double)byRecency.IndexOf(b) / layer.Count;

                    return Deficit(b.Id, minShown, maxShown)
                           + TieBreakWeight * priorityOrRecency
                           + Jitter();
                })
                .Select(b => ToDto(b, null))
                .ToList();
        }

        return ordered1.Concat(RankFlatLayer(layer2)).Concat(RankFlatLayer(layer3)).ToList();
    }

    private NearbyBannerDto ToDto(Banner banner, double? distanceKm)
    {
        var activeWindow = banner.BannerDurations
            .OrderBy(d => d.StartDate)
            .FirstOrDefault();

        return new NearbyBannerDto(
            banner.Id,
            banner.Name,
            banner.Phone,
            banner.ActionType,
            banner.ActionUrl,
            banner.TargetType,
            banner.TargetCity,
            banner.CenterLat,
            banner.CenterLng,
            banner.RadiusKm,
            banner.LinkedEntityType,
            banner.LinkedEntityId,
            banner.Priority,
            distanceKm is null ? null : Math.Round(distanceKm.Value, 2),
            banner.CampaignId,
            activeWindow?.StartDate.ToDateTime(TimeOnly.MinValue),
            activeWindow?.EndDate.ToDateTime(TimeOnly.MinValue),
            banner.BannerAttachments
                .Where(a => !a.IsDeleted)
                .Select(a => new BannerAttachmentSummery(
                    a.Id, a.ContentType, a.FileName, a.FileSize, a.FileExtension,
                    uploadFileService.GetFilePath(UploadFileFolders.CableBanners, a.FileName)))
                .ToList());
    }
}
