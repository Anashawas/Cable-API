using System.Text.RegularExpressions;
using Application.Common.Security;
using Application.Ocpp.Queries.GetSessionLive;
using Cable.Core;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.LiveCharging;

// ---------------------------------------------------------------------------
// §13.2 — the session receipt (PDF) for the driver, the station and the admin, and its public check.
// ---------------------------------------------------------------------------

/// <summary>CHS-YYYY-NNNNNN: the Jordan year the session started and its id (the OCPP transactionId), e.g. CHS-2026-000072. Pure.</summary>
public static partial class SessionReceiptNumber
{
    public static string For(int sessionId, DateTime startedAtUtc) =>
        $"CHS-{JordanTime.FromUtc(startedAtUtc).Year:D4}-{sessionId:D6}";

    /// <summary>Trimmed, upper case, "CHS-2026-000072"; null when it cannot be a receipt number.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim().ToUpperInvariant();
        return Shape().IsMatch(s) ? s : null;
    }

    [GeneratedRegex(@"^CHS-\d{4}-\d{6,10}$")]
    private static partial Regex Shape();
}

/// <summary>The public check behind the receipt's QR: enough for an accountant to confirm it, nothing personal but the first name.</summary>
public record SessionReceiptVerifyDto(
    string ReceiptNo,
    bool Valid,
    string StationName,
    string? ChargerName,
    int ConnectorId,
    DateTime StartedAt,
    DateTime? StoppedAt,
    decimal? EnergyKwh,
    int? TotalFils,
    decimal? TotalJod,
    string? DriverFirstName,
    DateTime? IssuedAt);

internal static class SessionReceiptBuilder
{
    public static async Task<LiveChargingFile> BuildAsync(IApplicationDbContext db, ILiveChargingPdfService pdf, int transactionId, int? chargingPointId, CancellationToken ct)
    {
        var tx = await db.OcppTransactions
            .Where(t => t.Id == transactionId && !t.WasRejected && !t.IsOrphan
                        && (chargingPointId == null || t.ChargePoint.ChargingPointId == chargingPointId))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Lang.Pick("ما لقينا هاي الجلسة.", "Cannot find this session."));
        if (tx.IsOpen)
            throw new UnprocessableException(LiveChargingErrorCodes.SessionOpen, Lang.Pick(
                "الجلسة لسا شغّالة. الإيصال بيطلع لما تخلص.",
                "The session is still running. The receipt is ready once it ends."));

        // The number is assigned on the first request and never changes.
        if (tx.ReceiptNo is null)
        {
            tx.ReceiptNo = SessionReceiptNumber.For(tx.Id, tx.StartedAt);
            tx.ReceiptIssuedAt = DateTime.UtcNow;
            await db.SaveChanges(ct);
        }

        var session = await SessionLiveBuilder.BuildAsync(db, tx.Id, null, ct);
        var info = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.Id == tx.Id)
            .Select(t => new
            {
                t.ChargePoint.ChargingPointId,
                StationName = t.ChargePoint.ChargingPoint.Name,
                t.ChargePoint.ChargingPoint.Address,
                t.ChargePoint.ChargingPoint.CityName,
                t.ChargePoint.DisplayName,
                Plug = db.OcppConnectors.Where(k => k.OcppChargePointId == t.OcppChargePointId && k.ConnectorId == t.ConnectorId)
                    .Select(k => new { PlugTypeName = k.PlugType != null ? k.PlugType.Name : null, k.PowerKw }).FirstOrDefault(),
                DriverName = t.StartSource == OcppStartSource.Operator ? null
                    : db.UserAccounts.Where(u => u.Id == t.StartedByUserId).Select(u => u.Name).FirstOrDefault(),
            })
            .FirstAsync(ct);

        var ordinals = await LiveChargingAccess.ChargerOrdinalsAsync(db, info.ChargingPointId, ct);
        var (ordinal, _, _) = ordinals.GetValueOrDefault(tx.OcppChargePointId, (1, null, ""));
        var chargerName = !string.IsNullOrWhiteSpace(info.DisplayName)
            ? info.DisplayName.Trim()
            : $"{LiveChargingAccess.ChargerLabel(null, ordinal, false)} / {LiveChargingAccess.ChargerLabel(null, ordinal, true)}";
        var address = string.Join(" · ", new[] { info.Address, info.CityName }.Where(x => !string.IsNullOrWhiteSpace(x)));

        var data = new SessionReceiptData(
            tx.ReceiptNo!,
            JordanTime.FromUtc(tx.ReceiptIssuedAt ?? DateTime.UtcNow),
            info.StationName,
            address.Length == 0 ? null : address,
            chargerName,
            tx.ConnectorId,
            info.Plug?.PlugTypeName,
            info.Plug?.PowerKw,
            info.DriverName,
            // carName: OcppTransaction has no car yet in this branch (another change adds CarId).
            null,
            JordanTime.FromUtc(session.StartedAt),
            session.StoppedAt is DateTime s ? JordanTime.FromUtc(s) : null,
            session.DurationSec,
            session.SocStartPercent,
            session.SocEndPercent,
            session.EnergyKwh,
            (session.Price ?? []).Select(p => new SessionReceiptLine(p.NameEn, p.NameAr, p.Kwh, p.PriceFils, p.Fils)).ToList(),
            session.CostFils,
            session.StopReasonText,
            session.StopReasonTextAr,
            LiveChargingLimits.ReceiptVerifyLinkBase + tx.ReceiptNo);

        return new LiveChargingFile(pdf.RenderSessionReceipt(data), "application/pdf", tx.ReceiptNo + ".pdf");
    }
}

/// <summary>Driver: the receipt of one of their finished sessions (PDF). 422 SESSION_OPEN while it runs.</summary>
public record GetMySessionReceiptRequest(int TransactionId) : IRequest<LiveChargingFile>;

public class GetMySessionReceiptRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser, ILiveChargingPdfService pdf)
    : IRequestHandler<GetMySessionReceiptRequest, LiveChargingFile>
{
    public async Task<LiveChargingFile> Handle(GetMySessionReceiptRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var mine = await db.OcppTransactions.AsNoTracking().AnyAsync(t => t.Id == request.TransactionId && t.StartedByUserId == userId, cancellationToken);
        if (!mine) throw new NotFoundException(Lang.Pick("ما لقينا هاي الجلسة بحسابك.", "Cannot find this session in your account."));
        return await SessionReceiptBuilder.BuildAsync(db, pdf, request.TransactionId, null, cancellationToken);
    }
}

/// <summary>Owner / manager (ConnectView): the same receipt for a session at their station.</summary>
public record GetStationSessionReceiptRequest(int ChargingPointId, int TransactionId) : IRequest<LiveChargingFile>;

public class GetStationSessionReceiptRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser, ILiveChargingPdfService pdf)
    : IRequestHandler<GetStationSessionReceiptRequest, LiveChargingFile>
{
    public async Task<LiveChargingFile> Handle(GetStationSessionReceiptRequest request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId, cancellationToken,
            requiredPrivilege: WorkerPrivileges.ConnectView);
        return await SessionReceiptBuilder.BuildAsync(db, pdf, request.TransactionId, request.ChargingPointId, cancellationToken);
    }
}

/// <summary>Admin: the receipt of any session.</summary>
public record GetAdminSessionReceiptRequest(int TransactionId) : IRequest<LiveChargingFile>;

public class GetAdminSessionReceiptRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser, ILiveChargingPdfService pdf)
    : IRequestHandler<GetAdminSessionReceiptRequest, LiveChargingFile>
{
    public async Task<LiveChargingFile> Handle(GetAdminSessionReceiptRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        return await SessionReceiptBuilder.BuildAsync(db, pdf, request.TransactionId, null, cancellationToken);
    }
}

/// <summary>Public (no sign-in): is this receipt genuine? Only receipts that were actually issued answer.</summary>
public record VerifySessionReceiptRequest(string ReceiptNo) : IRequest<SessionReceiptVerifyDto>;

public class VerifySessionReceiptRequestHandler(IApplicationDbContext db) : IRequestHandler<VerifySessionReceiptRequest, SessionReceiptVerifyDto>
{
    public async Task<SessionReceiptVerifyDto> Handle(VerifySessionReceiptRequest request, CancellationToken cancellationToken)
    {
        var no = SessionReceiptNumber.Normalize(request.ReceiptNo) ?? throw NotFound();
        var row = await db.OcppTransactions.AsNoTracking()
            .Where(t => t.ReceiptNo == no && !t.WasRejected && !t.IsOrphan)
            .Select(t => new
            {
                t.ReceiptNo, StationName = t.ChargePoint.ChargingPoint.Name, t.ChargePoint.DisplayName, t.ConnectorId, t.StartedAt, t.StoppedAt,
                t.EnergyKwh, t.CostFils, t.ReceiptIssuedAt, t.StartSource,
                DriverName = db.UserAccounts.Where(u => u.Id == t.StartedByUserId).Select(u => u.Name).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw NotFound();

        return new SessionReceiptVerifyDto(row.ReceiptNo!, true, row.StationName, row.DisplayName, row.ConnectorId, row.StartedAt, row.StoppedAt,
            row.EnergyKwh, row.CostFils, row.CostFils is int f ? f / 1000m : null,
            row.StartSource == OcppStartSource.Operator ? null : DriverPrivacy.FirstName(row.DriverName), row.ReceiptIssuedAt);
    }

    private static CodedNotFoundException NotFound() => new(LiveChargingErrorCodes.ReceiptNotFound, Lang.Pick(
        "ما لقينا إيصال بهاد الرقم.", "No receipt with this number."));
}
