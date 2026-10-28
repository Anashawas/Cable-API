using Cable.Core.Constants;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

/// <summary>§11.a — who charged, as the station's partner may see it: the name and a masked phone, never the full number or the user id.</summary>
public record SessionDriverDto(string? DisplayName, string? PhoneMasked);

/// <summary>Pure privacy rules for the partner views.</summary>
public static class DriverPrivacy
{
    /// <summary>
    /// 962791234567 / +962791234567 / 0791234567 → 079****567: the local form, first 3 and last 3 digits.
    /// Anything shorter than 7 digits → "****". Null / empty → null.
    /// </summary>
    public static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(PhoneNumberUtility.ToAsciiDigits(phone).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00962")) digits = digits[5..];
        else if (digits.StartsWith("962")) digits = digits[3..];
        if (digits.Length == 9 && digits[0] == '7') digits = "0" + digits;
        if (digits.Length < 7) return "****";
        return digits[..3] + "****" + digits[^3..];
    }

    /// <summary>Only the first word of the name (the public receipt check).</summary>
    public static string? FirstName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }
}

/// <summary>Driver / staff / card details for a set of sessions at one station, as the partner sees them.</summary>
public sealed record SessionPeople(SessionDriverDto? Driver, string? CardLabel, string? StartedByUserName);

public static class SessionPeopleLoader
{
    public sealed record SessionKey(int Id, string StartSource, int? StartedByUserId, string IdTag);

    /// <summary>
    /// App / sticker starts and cards linked to a driver → Driver (name + masked phone); operator starts →
    /// StartedByUserName (the staff member who pressed start); card sessions → CardLabel from the station's card list.
    /// </summary>
    public static async Task<Dictionary<int, SessionPeople>> LoadForPartnerAsync(IApplicationDbContext db, int chargingPointId,
        IReadOnlyCollection<SessionKey> sessions, CancellationToken ct)
    {
        var result = new Dictionary<int, SessionPeople>();
        if (sessions.Count == 0) return result;

        var userIds = sessions.Where(s => s.StartedByUserId != null).Select(s => s.StartedByUserId!.Value).Distinct().ToList();
        var users = userIds.Count == 0
            ? new Dictionary<int, (string? Name, string? Phone)>()
            : (await db.UserAccounts.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.Phone }).ToListAsync(ct))
                .ToDictionary(u => u.Id, u => (u.Name, u.Phone));

        var labels = await CardLabelsAsync(db, chargingPointId, sessions.Where(s => s.StartSource == OcppStartSource.Card).Select(s => s.IdTag), ct);

        foreach (var s in sessions)
        {
            (string? Name, string? Phone) user = s.StartedByUserId is int uid && users.TryGetValue(uid, out var u) ? u : (null, null);
            var isStaff = s.StartSource == OcppStartSource.Operator;
            var driver = !isStaff && s.StartedByUserId != null ? new SessionDriverDto(user.Name, DriverPrivacy.MaskPhone(user.Phone)) : null;
            var label = s.StartSource == OcppStartSource.Card ? labels.GetValueOrDefault(OcppCredentials.NormalizeIdTag(s.IdTag)) : null;
            result[s.Id] = new SessionPeople(driver, label, isStaff ? user.Name : null);
        }
        return result;
    }

    /// <summary>Normalised idTag → the label the station gave that card (OcppAuthorizedTag.Label).</summary>
    public static async Task<Dictionary<string, string?>> CardLabelsAsync(IApplicationDbContext db, int chargingPointId, IEnumerable<string> idTags, CancellationToken ct)
    {
        var tags = idTags.Select(OcppCredentials.NormalizeIdTag).Where(t => t.Length > 0).Distinct().ToList();
        if (tags.Count == 0) return new Dictionary<string, string?>();
        var rows = await db.OcppAuthorizedTags.AsNoTracking()
            .Where(a => a.ChargingPointId == chargingPointId && !a.IsDeleted && tags.Contains(a.IdTag))
            .Select(a => new { a.IdTag, a.Label })
            .ToListAsync(ct);
        return rows.GroupBy(r => OcppCredentials.NormalizeIdTag(r.IdTag)).ToDictionary(g => g.Key, g => g.First().Label);
    }
}
