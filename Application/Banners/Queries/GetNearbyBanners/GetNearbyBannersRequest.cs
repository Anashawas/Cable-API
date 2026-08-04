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
    private const double ProximityWeight = 0.7;
    private const double PacingWeight = 0.3;
    private const double JitterRange = 0.05;
    private const int PacingWindowDays = 7;

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

        // Pacing input: views per banner over the recent window, from the rollups.
        var ids = banners.Select(b => b.Id).ToList();
        var since = DateTime.UtcNow.Date.AddDays(-PacingWindowDays);
        var shownCounts = await applicationDbContext.AnalyticsDailyRollups.AsNoTracking()
            .Where(r => r.EntityType == "Banner"
                        && r.EventType == (int)AnalyticsEventType.BannerView
                        && ids.Contains(r.EntityId)
                        && r.Day >= since)
            .GroupBy(r => r.EntityId)
            .Select(g => new { g.Key, Count = g.Sum(x => x.Count) })
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

        double Pacing(int bannerId, int maxShown) =>
            maxShown <= 0 ? 1 : 1 - (double)shownCounts.GetValueOrDefault(bannerId, 0) / maxShown;

        double Jitter() => (Random.Shared.NextDouble() * 2 - 1) * JitterRange;

        // Layer 1: nearest wins — proximity-weighted, pacing keeps rotation fair.
        var max1 = layer1.Count == 0 ? 0 : layer1.Max(x => shownCounts.GetValueOrDefault(x.Banner.Id, 0));
        var ordered1 = layer1
            .OrderByDescending(x =>
                ProximityWeight * Math.Clamp(1 - x.Distance / x.EffectiveRadius, 0, 1)
                + PacingWeight * Pacing(x.Banner.Id, max1)
                + Jitter())
            .Select(x => ToDto(x.Banner, x.Distance))
            .ToList();

        List<NearbyBannerDto> RankFlatLayer(List<Banner> layer)
        {
            if (layer.Count == 0) return [];
            var maxShown = layer.Max(b => shownCounts.GetValueOrDefault(b.Id, 0));
            var maxPriority = layer.Max(b => b.Priority ?? 0);
            var byRecency = layer.OrderByDescending(b => b.CreatedAt).ToList();

            return layer
                .OrderByDescending(b =>
                {
                    var priorityOrRecency = maxPriority > 0
                        ? (double)(b.Priority ?? 0) / maxPriority
                        : 1 - (double)byRecency.IndexOf(b) / layer.Count;
                    return ProximityWeight * priorityOrRecency
                           + PacingWeight * Pacing(b.Id, maxShown)
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
