using Application.Common.Security;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Admin.Queries.GetPartnerAdoption;

/// <summary>Where a station sits in the adoption funnel. Ordered worst to best.</summary>
public static class AdoptionStage
{
    public const string NoOwner = "NoOwner";
    public const string DefaultOwner = "DefaultOwner";   // owned by an admin account — a placeholder, not a partner
    public const string NeverUsedApp = "NeverUsedApp";   // real owner who has never signed into a partner client
    public const string Inactive = "Inactive";           // used it, but not within the window
    public const string Active = "Active";
}

public record PartnerAdoptionStationDto(
    int ChargingPointId,
    string Name,
    string? CityName,
    int? OwnerId,
    string? OwnerName,
    string? OwnerPhone,
    bool IsDefaultOwner,
    bool OwnerUsesPartnerApp,
    bool OwnerUsesPartnerWeb,
    DateTime? OwnerLastLoginAt,
    DateTime? OwnerLastSeenAt,
    DateTime? LastPartnerActivityAt,
    string Stage);

public record PartnerAdoptionFunnelDto(
    int AllStations,
    int NoOwner,
    int DefaultOwner,
    int WithRealOwner,
    int DistinctRealOwners,
    int OwnerUsedPartnerApp,
    int ActiveInWindow);

public record PartnerAdoptionDto(
    PartnerAdoptionFunnelDto Funnel,
    int ActiveWindowDays,
    List<PartnerAdoptionStationDto> Stations);

/// <summary>
/// Admin-only. Every station with where its owner stands on the partner-app
/// funnel, plus the funnel totals. Lives on its own endpoint rather than on
/// GetAllChargingPoints because that list is public and owners' login times
/// are not.
///
/// Signals, all from existing data except the two partner-scoped stamps:
///  - default owner   = the owner account holds the Admin role (no hard-coded id)
///  - uses the app    = has ever held a partner mobile / partner web session
///  - last login/seen = PartnerLastLoginAt / PartnerLastSeenAt (partner clients only)
///  - last activity   = newest QR generated, update request, or offer proposed
/// </summary>
public record GetPartnerAdoptionRequest(int ActiveWindowDays = 30) : IRequest<PartnerAdoptionDto>;

public class GetPartnerAdoptionRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPartnerAdoptionRequest, PartnerAdoptionDto>
{
    public async Task<PartnerAdoptionDto> Handle(GetPartnerAdoptionRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var windowDays = request.ActiveWindowDays > 0 ? request.ActiveWindowDays : 30;
        var cutoff = DateTime.UtcNow.AddDays(-windowDays);

        var stations = await applicationDbContext.ChargingPoints.AsNoTracking()
            .Where(cp => !cp.IsDeleted)
            .Select(cp => new
            {
                cp.Id,
                cp.Name,
                cp.CityName,
                cp.OwnerId,
                Owner = cp.OwnerId == null
                    ? null
                    : applicationDbContext.UserAccounts
                        .Where(u => u.Id == cp.OwnerId)
                        .Select(u => new
                        {
                            u.Name,
                            u.Phone,
                            u.RoleId,
                            u.PartnerLastLoginAt,
                            u.PartnerLastSeenAt,
                            HasMobileSession = u.ProviderSecurityStamp != null,
                            HasWebSession = u.ProviderWebSecurityStamp != null
                        })
                        .FirstOrDefault()
            })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var ownerIds = stations.Where(s => s.OwnerId.HasValue).Select(s => s.OwnerId!.Value).Distinct().ToList();

        // Newest partner-side action per owner, across the four things a partner
        // does that leave a row. Four grouped queries, not one per station.
        var lastActivity = new Dictionary<int, DateTime>();
        void Merge(IEnumerable<KeyValuePair<int, DateTime>> rows)
        {
            foreach (var (id, at) in rows)
                if (!lastActivity.TryGetValue(id, out var cur) || at > cur) lastActivity[id] = at;
        }

        if (ownerIds.Count > 0)
        {
            Merge(await applicationDbContext.PartnerTransactions.AsNoTracking()
                .Where(t => t.ConfirmedByUserId != null && ownerIds.Contains(t.ConfirmedByUserId.Value))
                .GroupBy(t => t.ConfirmedByUserId!.Value)
                .Select(g => new KeyValuePair<int, DateTime>(g.Key, g.Max(t => t.CreatedAt)))
                .ToListAsync(cancellationToken));
            Merge(await applicationDbContext.OfferTransactions.AsNoTracking()
                .Where(t => t.ConfirmedByUserId != null && ownerIds.Contains(t.ConfirmedByUserId.Value))
                .GroupBy(t => t.ConfirmedByUserId!.Value)
                .Select(g => new KeyValuePair<int, DateTime>(g.Key, g.Max(t => t.CreatedAt)))
                .ToListAsync(cancellationToken));
            Merge(await applicationDbContext.ChargingPointUpdateRequests.AsNoTracking()
                .Where(r => ownerIds.Contains(r.RequestedByUserId))
                .GroupBy(r => r.RequestedByUserId)
                .Select(g => new KeyValuePair<int, DateTime>(g.Key, g.Max(r => r.CreatedAt)))
                .ToListAsync(cancellationToken));
            Merge(await applicationDbContext.ProviderOffers.AsNoTracking()
                .Where(o => ownerIds.Contains(o.ProposedByUserId))
                .GroupBy(o => o.ProposedByUserId)
                .Select(g => new KeyValuePair<int, DateTime>(g.Key, g.Max(o => o.CreatedAt)))
                .ToListAsync(cancellationToken));
        }

        var rows = new List<PartnerAdoptionStationDto>(stations.Count);
        foreach (var s in stations)
        {
            var o = s.Owner;
            var isDefault = o != null && o.RoleId == AdminRoleGuard.AdminRoleId;
            DateTime? activity = s.OwnerId.HasValue && lastActivity.TryGetValue(s.OwnerId.Value, out var a) ? a : null;

            string stage;
            if (o == null) stage = AdoptionStage.NoOwner;
            else if (isDefault) stage = AdoptionStage.DefaultOwner;
            else
            {
                // Generating a QR, submitting an update request or proposing an
                // offer can only be done from a partner client, so activity is
                // evidence of app use even for a session that predates the stamps.
                var usedApp = o.HasMobileSession || o.HasWebSession || o.PartnerLastLoginAt != null || activity != null;
                if (!usedApp) stage = AdoptionStage.NeverUsedApp;
                else
                {
                    var active = (o.PartnerLastSeenAt != null && o.PartnerLastSeenAt >= cutoff)
                                 || (activity != null && activity >= cutoff);
                    stage = active ? AdoptionStage.Active : AdoptionStage.Inactive;
                }
            }

            rows.Add(new PartnerAdoptionStationDto(
                s.Id, s.Name, s.CityName,
                s.OwnerId, o?.Name, PhoneNumberUtility.ToE164OrOriginal(o?.Phone),
                isDefault,
                o?.HasMobileSession ?? false,
                o?.HasWebSession ?? false,
                o?.PartnerLastLoginAt,
                o?.PartnerLastSeenAt,
                activity,
                stage));
        }

        var real = rows.Where(r => r.Stage is not (AdoptionStage.NoOwner or AdoptionStage.DefaultOwner)).ToList();
        var funnel = new PartnerAdoptionFunnelDto(
            AllStations: rows.Count,
            NoOwner: rows.Count(r => r.Stage == AdoptionStage.NoOwner),
            DefaultOwner: rows.Count(r => r.Stage == AdoptionStage.DefaultOwner),
            WithRealOwner: real.Count,
            DistinctRealOwners: real.Select(r => r.OwnerId).Distinct().Count(),
            OwnerUsedPartnerApp: real.Count(r => r.Stage is AdoptionStage.Active or AdoptionStage.Inactive),
            ActiveInWindow: real.Count(r => r.Stage == AdoptionStage.Active));

        return new PartnerAdoptionDto(funnel, windowDays, rows);
    }
}
