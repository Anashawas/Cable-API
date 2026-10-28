using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

/// <summary>
/// §10 — sticker codes per plug: issued when Cable.Ocpp first sees a plug (StatusNotification), backfilled by the
/// Sprint5_LiveCharging_B script, and topped up whenever a station's codes are listed (a plug added later never
/// stays without one). Revoke and renew keeps the old row so its scan answers "revoked".
/// </summary>
public static class ConnectorCodeService
{
    private const int MaxAttempts = 12;

    /// <summary>A code not used by any row (active or revoked) nor by <paramref name="reserved"/> (codes of the same batch).</summary>
    public static async Task<string> NewUniqueCodeAsync(IApplicationDbContext db, ISet<string>? reserved, CancellationToken ct)
    {
        for (var i = 0; i < MaxAttempts; i++)
        {
            var code = ConnectorCodeFormat.Generate();
            if (reserved?.Contains(code) == true) continue;
            if (await db.OcppConnectorCodes.AsNoTracking().AnyAsync(c => c.Code == code, ct)) continue;
            reserved?.Add(code);
            return code;
        }
        throw new InvalidOperationException("Could not generate a unique plug code.");
    }

    /// <summary>
    /// For a plug row being created right now (not saved yet): queue its code in the same SaveChanges.
    /// Connector 0 (the charger itself) gets none. Never throws — a missing code is filled in when the station's codes are listed.
    /// </summary>
    public static async Task AttachToNewConnectorAsync(IApplicationDbContext db, OcppConnector connector, CancellationToken ct)
    {
        if (connector.ConnectorId < 1) return;
        try
        {
            db.OcppConnectorCodes.Add(new OcppConnectorCode { Connector = connector, Code = await NewUniqueCodeAsync(db, null, ct) });
        }
        catch (InvalidOperationException)
        {
            // Practically unreachable (31^6 codes); the listing backfill covers it.
        }
    }

    /// <summary>Gives every plug of the station's chargers (ConnectorId &gt; 0) without an active code a new one. Returns how many were issued.</summary>
    public static async Task<int> EnsureForStationAsync(IApplicationDbContext db, int chargingPointId, CancellationToken ct)
    {
        var missing = await db.OcppConnectors
            .Where(k => k.ConnectorId > 0 && k.ChargePoint.ChargingPointId == chargingPointId && !k.ChargePoint.IsDeleted
                        && !db.OcppConnectorCodes.Any(c => c.OcppConnectorId == k.Id && c.RevokedAt == null && !c.IsDeleted))
            .Select(k => k.Id)
            .ToListAsync(ct);
        if (missing.Count == 0) return 0;

        var reserved = new HashSet<string>();
        foreach (var connectorId in missing)
            db.OcppConnectorCodes.Add(new OcppConnectorCode { OcppConnectorId = connectorId, Code = await NewUniqueCodeAsync(db, reserved, ct) });
        await db.SaveChanges(ct);
        return missing.Count;
    }

    public sealed record ResolvedCode(int CodeId, string Code, int OcppConnectorId, int OcppChargePointId, int ConnectorId, int ChargingPointId);

    /// <summary>
    /// The plug behind a scanned / typed code. 404 CODE_NOT_FOUND when it is not a Cable plug code (or its charger was
    /// removed), 410 CODE_REVOKED when the code was revoked (stolen / leaked sticker replaced).
    /// </summary>
    public static async Task<ResolvedCode> ResolveAsync(IApplicationDbContext db, string? input, CancellationToken ct)
    {
        var code = ConnectorCodeFormat.Normalize(input)
                   ?? throw NotFound();

        var row = await db.OcppConnectorCodes.AsNoTracking()
            .Where(c => c.Code == code && !c.IsDeleted && !c.Connector.ChargePoint.IsDeleted)
            .Select(c => new
            {
                c.Id, c.Code, c.RevokedAt, c.OcppConnectorId, c.Connector.OcppChargePointId, c.Connector.ConnectorId,
                c.Connector.ChargePoint.ChargingPointId,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw NotFound();

        if (row.RevokedAt is not null)
            throw new GoneException(LiveChargingErrorCodes.CodeRevoked, Lang.Pick(
                "هاد الكود مش صالح. استعمل الكود الجديد الملصوق على المقبس، أو اختار المقبس من صفحة المحطة.",
                "This code is no longer valid. Use the new code on the plug, or pick the plug from the station page."));

        return new ResolvedCode(row.Id, row.Code, row.OcppConnectorId, row.OcppChargePointId, row.ConnectorId, row.ChargingPointId);
    }

    /// <summary>A start that came with a sticker code must be for that very plug.</summary>
    public static async Task EnsureMatchesAsync(IApplicationDbContext db, string connectorCode, int ocppChargePointId, int connectorId, CancellationToken ct)
    {
        var resolved = await ResolveAsync(db, connectorCode, ct);
        if (resolved.OcppChargePointId != ocppChargePointId || resolved.ConnectorId != connectorId)
            throw new UnprocessableException(LiveChargingErrorCodes.CodeMismatch, Lang.Pick(
                "الكود الممسوح لمقبس ثاني. امسح الكود اللي على نفس المقبس.",
                "The scanned code belongs to another plug. Scan the code on this plug."));
    }

    private static CodedNotFoundException NotFound() => new(LiveChargingErrorCodes.CodeNotFound, Lang.Pick(
        "هاد الكود مش من كيبل. تأكّد منه وجرّب مرة ثانية.",
        "This is not a Cable plug code. Check it and try again."));
}
