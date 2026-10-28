using Cable.Core.Constants;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

/// <summary>§11.a — the extra filters and the driver / card / staff details of the partner's session list.</summary>
public static class PartnerSessionFilters
{
    /// <param name="status">open | closed (anything else = both).</param>
    /// <param name="q">Contained in the driver's or staff member's name, or in the card's label at this station.</param>
    public static IQueryable<OcppTransaction> Apply(IApplicationDbContext db, IQueryable<OcppTransaction> q, int chargingPointId,
        int? connectorId, string? startMethod, string? status, string? search)
    {
        if (connectorId is int c) q = q.Where(t => t.ConnectorId == c);

        var method = OcppStartMethod.Parse(startMethod);
        if (method is not null) q = q.Where(t => (t.StartMethod ?? t.StartSource) == method);

        q = ApplyStatus(q, status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(t =>
                db.UserAccounts.Any(u => u.Id == t.StartedByUserId && u.Name != null && u.Name.Contains(term))
                || db.OcppAuthorizedTags.Any(a => a.ChargingPointId == chargingPointId && !a.IsDeleted && a.IdTag == t.IdTag && a.Label != null && a.Label.Contains(term)));
        }
        return q;
    }

    public static IQueryable<OcppTransaction> ApplyStatus(IQueryable<OcppTransaction> q, string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "open" => q.Where(t => t.IsOpen),
            "closed" => q.Where(t => !t.IsOpen),
            _ => q,
        };

    public static async Task<Dictionary<int, SessionPeople>> PeopleAsync(IApplicationDbContext db, int chargingPointId, List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, SessionPeople>();
        var keys = await db.OcppTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new SessionPeopleLoader.SessionKey(t.Id, t.StartSource, t.StartedByUserId, t.IdTag))
            .ToListAsync(ct);
        return await SessionPeopleLoader.LoadForPartnerAsync(db, chargingPointId, keys, ct);
    }

    /// <summary>The owner's session detail with the driver / card / staff details and the start method.</summary>
    public static async Task<Application.Ocpp.Queries.GetSessionLive.SessionLiveDto> WithPeopleAsync(IApplicationDbContext db,
        Application.Ocpp.Queries.GetSessionLive.SessionLiveDto dto, CancellationToken ct)
    {
        var method = await db.OcppTransactions.AsNoTracking().Where(t => t.Id == dto.Id)
            .Select(t => t.StartMethod ?? t.StartSource).FirstOrDefaultAsync(ct);
        var people = (await PeopleAsync(db, dto.ChargingPointId, [dto.Id], ct)).GetValueOrDefault(dto.Id);
        return dto with { Driver = people?.Driver, CardLabel = people?.CardLabel, StartedByUserName = people?.StartedByUserName, StartMethod = method };
    }
}
