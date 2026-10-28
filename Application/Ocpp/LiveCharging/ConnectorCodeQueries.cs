using Application.Common.Security;
using Application.Ocpp.Queries.GetStationLive;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// §10 — CHG- plug stickers: resolve (driver app), list / revoke-and-renew / print (admin), list / print (owner).
// ---------------------------------------------------------------------------

/// <param name="CanStartFromApp">The same answer as PlugLiveDto.canStartFromApp in /live.</param>
/// <param name="CannotStartReason">offline | plugCable | busy | outOfOrder (the plug), or noSubscription | notShared | blocked (the station does not offer app charging).</param>
/// <param name="SuspectedIssue">Drivers reported this plug 3 times within an hour (warning only).</param>
public record ConnectorCodeResolveDto(
    string Code,
    int ChargingPointId,
    string StationName,
    int ChargerId,
    string ChargerName,
    int ChargerOrdinal,
    int ConnectorId,
    int? PlugTypeId,
    string? PlugTypeName,
    decimal? PowerKw,
    string State,
    bool CanStartFromApp,
    string? CannotStartReason,
    bool SuspectedIssue = false);

/// <summary>Station-level reasons the resolve answers when the station does not offer app charging.</summary>
public static class StationStartability
{
    public const string NoSubscription = "noSubscription";
    public const string NotShared = "notShared";
    public const string Blocked = "blocked";
}

/// <summary>Driver app (no sign-in needed): what a scanned / typed CHG- code points at — straight into the start sheet.</summary>
public record ResolveConnectorCodeRequest(string? Code) : IRequest<ConnectorCodeResolveDto>;

public class ResolveConnectorCodeRequestHandler(IApplicationDbContext db) : IRequestHandler<ResolveConnectorCodeRequest, ConnectorCodeResolveDto>
{
    public async Task<ConnectorCodeResolveDto> Handle(ResolveConnectorCodeRequest request, CancellationToken cancellationToken)
    {
        var r = await ConnectorCodeService.ResolveAsync(db, request.Code, cancellationToken);
        var now = DateTime.UtcNow;

        var plug = await db.OcppConnectors.AsNoTracking()
            .Where(k => k.Id == r.OcppConnectorId)
            .Select(k => new
            {
                k.Status, k.ErrorCode, k.PlugTypeId, PlugTypeName = k.PlugType != null ? k.PlugType.Name : null, k.PowerKw,
                k.ChargePoint.IsEnabled, k.ChargePoint.IsConnected, k.ChargePoint.LastMessageAt, k.ChargePoint.DisconnectedAt, k.ChargePoint.HeartbeatInterval,
                StationName = k.ChargePoint.ChargingPoint.Name,
            })
            .FirstAsync(cancellationToken);

        var ordinals = await LiveChargingAccess.ChargerOrdinalsAsync(db, r.ChargingPointId, cancellationToken);
        var (ordinal, displayName, _) = ordinals.GetValueOrDefault(r.OcppChargePointId, (1, null, ""));
        var chargerName = LiveChargingAccess.ChargerLabel(displayName, ordinal, Lang.IsArabic);

        var online = plug.IsEnabled && OcppLiveness.StateOf(plug.IsConnected, plug.LastMessageAt, plug.DisconnectedAt, plug.HeartbeatInterval, now) == OcppLiveness.Online;
        var visibility = await OcppLiveVisibility.GetAsync(db, r.ChargingPointId, cancellationToken);

        string state;
        string? reason;
        if (!visibility.VisibleToDrivers)
        {
            // Not shared with drivers: no live state either, only what the sticker itself says.
            state = PlugLiveState.Unknown;
            reason = !visibility.SubscriptionOn ? StationStartability.NoSubscription
                   : visibility.AdminBlocked ? StationStartability.Blocked
                   : StationStartability.NotShared;
        }
        else
        {
            var open = await db.OcppTransactions.AsNoTracking()
                .AnyAsync(t => t.OcppChargePointId == r.OcppChargePointId && t.ConnectorId == r.ConnectorId && t.IsOpen && !t.WasRejected && !t.IsStale, cancellationToken);
            state = PlugLiveState.Of(plug.Status, online);
            reason = PlugStartability.BlockedReason(plug.Status, plug.ErrorCode, online, open);
        }

        var stats = await ConnectorReportStats.LoadAsync(db, [r.OcppChargePointId], now, cancellationToken);
        var suspected = stats.TryGetValue((r.OcppChargePointId, r.ConnectorId), out var s) && s.SuspectedIssue;

        return new ConnectorCodeResolveDto(r.Code, r.ChargingPointId, plug.StationName, r.OcppChargePointId, chargerName, ordinal, r.ConnectorId,
            plug.PlugTypeId, plug.PlugTypeName, plug.PowerKw, state, reason is null, reason, suspected);
    }
}

/// <param name="ChargePointId">The charger's OCPP id (admin / owner only — never shown to drivers).</param>
/// <param name="RevokedCodes">How many earlier codes of this plug were revoked.</param>
public record ConnectorCodeDto(
    int CodeId,
    string Code,
    string Link,
    int ChargerId,
    string ChargerName,
    string ChargePointId,
    int ConnectorId,
    int? PlugTypeId,
    string? PlugTypeName,
    decimal? PowerKw,
    DateTime IssuedAt,
    int RevokedCodes,
    DateTime? LastRevokedAt);

internal static class ConnectorCodeList
{
    public static async Task<List<ConnectorCodeDto>> BuildAsync(IApplicationDbContext db, int chargingPointId, int? chargerId, CancellationToken ct)
    {
        var exists = await db.ChargingPoints.AsNoTracking().AnyAsync(c => c.Id == chargingPointId && !c.IsDeleted, ct);
        if (!exists) throw new NotFoundException("cannot find charging point " + chargingPointId);

        // Plugs added since the backfill (or a code lost to a failed insert) get theirs now.
        await ConnectorCodeService.EnsureForStationAsync(db, chargingPointId, ct);

        var rows = await db.OcppConnectorCodes.AsNoTracking()
            .Where(c => !c.IsDeleted && c.Connector.ConnectorId > 0 && c.Connector.ChargePoint.ChargingPointId == chargingPointId && !c.Connector.ChargePoint.IsDeleted
                        && (chargerId == null || c.Connector.OcppChargePointId == chargerId))
            .Select(c => new
            {
                c.Id, c.Code, c.RevokedAt, c.CreatedAt, c.OcppConnectorId, c.Connector.OcppChargePointId, c.Connector.ConnectorId,
                c.Connector.PlugTypeId, PlugTypeName = c.Connector.PlugType != null ? c.Connector.PlugType.Name : null, c.Connector.PowerKw,
            })
            .ToListAsync(ct);

        var ordinals = await LiveChargingAccess.ChargerOrdinalsAsync(db, chargingPointId, ct);
        return rows.Where(r => r.RevokedAt is null)
            .Select(r =>
            {
                var (ordinal, name, ocppId) = ordinals.GetValueOrDefault(r.OcppChargePointId, (0, null, ""));
                var revoked = rows.Where(x => x.OcppConnectorId == r.OcppConnectorId && x.RevokedAt != null).ToList();
                return new ConnectorCodeDto(r.Id, r.Code, ConnectorCodeFormat.LinkFor(r.Code), r.OcppChargePointId,
                    LiveChargingAccess.ChargerLabel(name, ordinal, Lang.IsArabic), ocppId, r.ConnectorId, r.PlugTypeId, r.PlugTypeName, r.PowerKw,
                    r.CreatedAt, revoked.Count, revoked.Max(x => x.RevokedAt));
            })
            .OrderBy(d => ordinals.GetValueOrDefault(d.ChargerId).Ordinal).ThenBy(d => d.ConnectorId)
            .ToList();
    }

    public static async Task<LiveChargingFile> StickersAsync(IApplicationDbContext db, ILiveChargingPdfService pdf, int chargingPointId, int? chargerId, CancellationToken ct)
    {
        var codes = await BuildAsync(db, chargingPointId, chargerId, ct);
        var stationName = await db.ChargingPoints.AsNoTracking().Where(c => c.Id == chargingPointId).Select(c => c.Name).FirstAsync(ct);
        var ordinals = await LiveChargingAccess.ChargerOrdinalsAsync(db, chargingPointId, ct);

        var stickers = codes.Select(c =>
        {
            var (ordinal, name, _) = ordinals.GetValueOrDefault(c.ChargerId, (0, null, ""));
            return new ConnectorSticker(c.Code, c.Link, stationName,
                LiveChargingAccess.ChargerLabel(name, ordinal, arabic: false), LiveChargingAccess.ChargerLabel(name, ordinal, arabic: true),
                c.ConnectorId, c.PlugTypeName, c.PowerKw);
        }).ToList();
        if (stickers.Count == 0)
            throw new DataValidationException("ChargingPointId", Lang.Pick(
                "ما في مقابس لهاي المحطة لسا. الشاحن لازم يتصل مرة وحدة على الأقل.",
                "This station has no plugs yet. The charger must connect at least once."));

        var bytes = pdf.RenderStickerSheet(new StickerSheetData(stationName, JordanTime.FromUtc(DateTime.UtcNow), stickers));
        return new LiveChargingFile(bytes, "application/pdf", $"stickers-station-{chargingPointId}.pdf");
    }
}

/// <summary>Admin: every plug's active code at a station (issuing missing ones on the way).</summary>
public record GetAdminConnectorCodesRequest(int ChargingPointId, int? ChargerId = null) : IRequest<List<ConnectorCodeDto>>;

public class GetAdminConnectorCodesRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetAdminConnectorCodesRequest, List<ConnectorCodeDto>>
{
    public async Task<List<ConnectorCodeDto>> Handle(GetAdminConnectorCodesRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return await ConnectorCodeList.BuildAsync(db, request.ChargingPointId, request.ChargerId, cancellationToken);
    }
}

/// <summary>Admin: revoke one plug's code (stolen / leaked sticker) and issue a new one. The old code then answers 410 CODE_REVOKED.</summary>
public record RenewConnectorCodeCommand(int ChargingPointId, int CodeId) : IRequest<ConnectorCodeDto>;

public class RenewConnectorCodeCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<RenewConnectorCodeCommand, ConnectorCodeDto>
{
    public async Task<ConnectorCodeDto> Handle(RenewConnectorCodeCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var row = await db.OcppConnectorCodes
            .Where(c => c.Id == request.CodeId && !c.IsDeleted && c.Connector.ChargePoint.ChargingPointId == request.ChargingPointId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("cannot find plug code " + request.CodeId + " at this station");
        if (row.RevokedAt is not null)
            throw new DataValidationException(nameof(request.CodeId), Lang.Pick("هاد الكود ملغي أصلاً.", "This code is already revoked."));

        // Revoke first and save, so the filtered unique index (one active code per plug) never sees two.
        row.RevokedAt = DateTime.UtcNow;
        row.RevokedBy = currentUser.UserId;
        await db.SaveChanges(cancellationToken);

        var fresh = new OcppConnectorCode { OcppConnectorId = row.OcppConnectorId, Code = await ConnectorCodeService.NewUniqueCodeAsync(db, null, cancellationToken) };
        db.OcppConnectorCodes.Add(fresh);
        await db.SaveChanges(cancellationToken);

        var list = await ConnectorCodeList.BuildAsync(db, request.ChargingPointId, null, cancellationToken);
        return list.First(c => c.CodeId == fresh.Id);
    }
}

/// <summary>Admin: the sticker sheet (A4, QR + code + station + charger + plug number + type) for a station, or one charger.</summary>
public record GetAdminStickerSheetRequest(int ChargingPointId, int? ChargerId = null) : IRequest<LiveChargingFile>;

public class GetAdminStickerSheetRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser, ILiveChargingPdfService pdf)
    : IRequestHandler<GetAdminStickerSheetRequest, LiveChargingFile>
{
    public async Task<LiveChargingFile> Handle(GetAdminStickerSheetRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return await ConnectorCodeList.StickersAsync(db, pdf, request.ChargingPointId, request.ChargerId, cancellationToken);
    }
}

/// <summary>Owner (not workers): their station's plug codes.</summary>
public record GetMyConnectorCodesRequest(int ChargingPointId, int? ChargerId = null) : IRequest<List<ConnectorCodeDto>>;

public class GetMyConnectorCodesRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetMyConnectorCodesRequest, List<ConnectorCodeDto>>
{
    public async Task<List<ConnectorCodeDto>> Handle(GetMyConnectorCodesRequest request, CancellationToken cancellationToken)
    {
        await LiveChargingAccess.EnsureStationOwnerAsync(db, currentUser, request.ChargingPointId, cancellationToken);
        return await ConnectorCodeList.BuildAsync(db, request.ChargingPointId, request.ChargerId, cancellationToken);
    }
}

/// <summary>Owner (not workers): print their station's stickers.</summary>
public record GetMyStickerSheetRequest(int ChargingPointId, int? ChargerId = null) : IRequest<LiveChargingFile>;

public class GetMyStickerSheetRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser, ILiveChargingPdfService pdf)
    : IRequestHandler<GetMyStickerSheetRequest, LiveChargingFile>
{
    public async Task<LiveChargingFile> Handle(GetMyStickerSheetRequest request, CancellationToken cancellationToken)
    {
        await LiveChargingAccess.EnsureStationOwnerAsync(db, currentUser, request.ChargingPointId, cancellationToken);
        return await ConnectorCodeList.StickersAsync(db, pdf, request.ChargingPointId, request.ChargerId, cancellationToken);
    }
}
