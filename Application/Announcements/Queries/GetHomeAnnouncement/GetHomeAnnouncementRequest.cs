using Application.Common;
using Application.Common.Interfaces;
using Application.Settings;
using Microsoft.EntityFrameworkCore;

namespace Application.Announcements.Queries.GetHomeAnnouncement;

// FLAT bilingual fields per the client contract (§5.2) — no nested objects.
public record HomeAnnouncementDto(
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
    int? AdvertiserId);

/// <summary>
/// The BE-decided home takeover: evaluates targeting (radius beats city beats
/// national), audience and per-user frequency caps, returns AT MOST one
/// message — or null when nothing qualifies. Serving to a logged-in user
/// records the show (that's what the caps count).
/// </summary>
public record GetHomeAnnouncementRequest(double? Lat, double? Lng, string? City)
    : IRequest<HomeAnnouncementDto?>;

public class GetHomeAnnouncementRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetHomeAnnouncementRequest, HomeAnnouncementDto?>
{
    public async Task<HomeAnnouncementDto?> Handle(GetHomeAnnouncementRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var userId = currentUserService.UserId;

        var candidates = await applicationDbContext.Announcements.AsNoTracking()
            .Where(a => !a.IsDeleted && a.IsActive
                        && a.StartDate <= now
                        && (a.EndDate == null || a.EndDate >= now))
            .ToListAsync(cancellationToken);

        // Audience
        candidates = candidates.Where(a => a.Audience switch
        {
            "guests" => userId is null,
            "loggedIn" => userId is not null,
            _ => true
        }).ToList();

        if (candidates.Count == 0)
            return null;

        var adminRadius = await AppSettingsProvider.GetNearbyRadiusKmAsync(applicationDbContext, cancellationToken);
        var city = request.City?.Trim();

        // Targeting: rank 0 = radius hit (closest wins), 1 = city match, 2 = national.
        var targeted = new List<(Domain.Enitites.Announcement A, int Rank, double Distance)>();
        foreach (var a in candidates)
        {
            switch (a.TargetType)
            {
                case "radius" when a.CenterLat.HasValue && a.CenterLng.HasValue:
                    if (request.Lat.HasValue && request.Lng.HasValue)
                    {
                        var d = GeoDistance.Km(request.Lat.Value, request.Lng.Value, a.CenterLat.Value, a.CenterLng.Value);
                        if (d <= (a.RadiusKm is > 0 ? a.RadiusKm.Value : adminRadius))
                            targeted.Add((a, 0, d));
                    }
                    break;
                case "city" when !string.IsNullOrEmpty(a.TargetCity):
                    if (!string.IsNullOrEmpty(city)
                        && string.Equals(a.TargetCity.Trim(), city, StringComparison.OrdinalIgnoreCase))
                        targeted.Add((a, 1, 0));
                    break;
                default:
                    targeted.Add((a, 2, 0));
                    break;
            }
        }

        if (targeted.Count == 0)
            return null;

        // Frequency caps — enforceable only for identified users.
        Dictionary<int, Domain.Enitites.AnnouncementUserState> states = new();
        var jordanToday = now.AddHours(3).Date;
        if (userId is not null)
        {
            var ids = targeted.Select(t => t.A.Id).ToList();
            states = await applicationDbContext.AnnouncementUserStates.AsNoTracking()
                .Where(s => s.UserId == userId.Value && ids.Contains(s.AnnouncementId))
                .ToDictionaryAsync(s => s.AnnouncementId, cancellationToken);

            targeted = targeted.Where(t =>
            {
                if (!states.TryGetValue(t.A.Id, out var s)) return true;
                if (t.A.MaxLifetime > 0 && s.ShownCount >= t.A.MaxLifetime) return false;
                if (t.A.StopOnDismiss && s.DismissedCount > 0) return false;
                if (t.A.CooldownHours > 0 && s.LastShownAt is not null
                    && s.LastShownAt.Value.AddHours(t.A.CooldownHours) > now) return false;
                if (t.A.MaxPerDay > 0 && s.LastShownAt is not null
                    && s.LastShownAt.Value.AddHours(3).Date == jordanToday
                    && s.DailyShownCount >= t.A.MaxPerDay) return false;
                return true;
            }).ToList();

            if (targeted.Count == 0)
                return null;
        }

        var winner = targeted
            .OrderBy(t => t.Rank)
            .ThenBy(t => t.Distance)
            .ThenByDescending(t => t.A.CreatedAt)
            .First().A;

        // Record the show for identified users — the caps count serves, so a
        // served message is a shown message.
        if (userId is not null)
        {
            var tracked = await applicationDbContext.AnnouncementUserStates
                .FirstOrDefaultAsync(s => s.UserId == userId.Value && s.AnnouncementId == winner.Id, cancellationToken);
            if (tracked is null)
            {
                applicationDbContext.AnnouncementUserStates.Add(new Domain.Enitites.AnnouncementUserState
                {
                    AnnouncementId = winner.Id,
                    UserId = userId.Value,
                    ShownCount = 1,
                    DailyShownCount = 1,
                    LastShownAt = now
                });
            }
            else
            {
                var sameDay = tracked.LastShownAt is not null && tracked.LastShownAt.Value.AddHours(3).Date == jordanToday;
                tracked.ShownCount += 1;
                tracked.DailyShownCount = sameDay ? tracked.DailyShownCount + 1 : 1;
                tracked.LastShownAt = now;
            }
            await applicationDbContext.SaveChanges(cancellationToken);
        }

        return new HomeAnnouncementDto(
            winner.Id,
            winner.TitleEn,
            winner.TitleAr,
            winner.BodyEn,
            winner.BodyAr,
            winner.ImageUrl,
            winner.ActionType,
            winner.ActionUrl,
            winner.ActionLabelEn,
            winner.ActionLabelAr,
            winner.TargetType,
            winner.TargetCity,
            winner.CenterLat,
            winner.CenterLng,
            winner.RadiusKm,
            winner.Audience,
            winner.StartDate,
            winner.EndDate,
            winner.MaxPerDay,
            winner.CooldownHours,
            winner.MaxLifetime,
            winner.StopOnDismiss,
            winner.IsActive,
            winner.CampaignId,
            winner.AdvertiserId);
    }
}
