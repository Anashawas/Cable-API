using Application.Ocpp.LiveCharging;
using Application.UserComplaints.Queries.GetAllUserComplaints;

namespace Application.UserComplaints.Queries;

/// <summary>Sprint 5 (§13.1): attaches the charging session to "problem with this session" complaints in the existing lists.</summary>
internal static class UserComplaintSessions
{
    public static async Task<List<GetUserComplaintsDto>> WithSessionsAsync(IApplicationDbContext db, List<GetUserComplaintsDto> items, CancellationToken ct)
    {
        var sessions = await ComplaintSessionEnricher.LoadAsync(db, items.Select(i => i.OcppTransactionId), ct);
        if (sessions.Count == 0) return items;
        return items.Select(i => i.OcppTransactionId is int id && sessions.TryGetValue(id, out var s) ? i with { Session = s } : i).ToList();
    }
}
