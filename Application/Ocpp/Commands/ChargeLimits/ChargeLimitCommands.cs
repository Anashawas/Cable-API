using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Commands.ChargeLimits;

// ---------------------------------------------------------------------------
// Charge limit on a running session: "stop at 7 JOD / 20 kWh / 80 %". Set, change or clear it
// while the session runs; Cable.Ocpp checks it on every reading and stops the session itself.
// A limit chosen with a remote start goes through OcppRemoteStart.SetPendingLimitAsync instead.
// ---------------------------------------------------------------------------

/// <summary>The limit as the pages show it. Kind null = no limit.</summary>
public record ChargeLimitDto(string? Kind, decimal? Value, string? Text, string? TextAr, DateTime? SetAt, DateTime? ReachedAt)
{
    public static ChargeLimitDto? From(string? kind, decimal? value, DateTime? setAt, DateTime? reachedAt) =>
        kind is null ? null
            : new ChargeLimitDto(kind, value, OcppChargeLimit.Describe(kind, value), OcppChargeLimit.DescribeAr(kind, value), setAt, reachedAt);

    /// <summary>"Stopped from the app" becomes "Charge limit reached (7.000 JOD)" when our own limit stop ended it.</summary>
    public static (string? En, string? Ar) StopText(string? stopReason, string? kind, decimal? value, DateTime? reachedAt) =>
        reachedAt is not null && stopReason == "Remote" && kind is not null
            ? ($"Charge limit reached ({OcppChargeLimit.Describe(kind, value)})", $"وصل إلى الحد المطلوب ({OcppChargeLimit.DescribeAr(kind, value)})")
            : (Cable.Core.Constants.OcppStopReason.Describe(stopReason), Cable.Core.Constants.OcppStopReason.DescribeAr(stopReason));
}

/// <summary>Body of the set-limit endpoints: kind Jod | Kwh | Soc and its value; kind null clears the limit.</summary>
public record ChargeLimitRequest(string? Kind, decimal? Value);

internal static class ChargeLimitSetter
{
    public static async Task<ChargeLimitDto?> ApplyAsync(IApplicationDbContext db, OcppTransaction tx, string? kind, decimal? value,
        int? userId, CancellationToken ct)
    {
        if (!tx.IsOpen) throw new DataValidationException("TransactionId", "This session is already finished.");
        if (tx.WasRejected) throw new DataValidationException("TransactionId", "This session was refused by the charger.");

        if (string.IsNullOrWhiteSpace(kind))
        {
            tx.ChargeLimitKind = null;
            tx.ChargeLimitValue = null;
            tx.ChargeLimitSetBy = userId;
            tx.ChargeLimitSetAt = DateTime.UtcNow;
            tx.ChargeLimitReachedAt = null;
            await db.SaveChanges(ct);
            return null;
        }

        var (k, v) = OcppChargeLimit.Validate(kind, value);
        tx.ChargeLimitKind = k;
        tx.ChargeLimitValue = v;
        tx.ChargeLimitSetBy = userId;
        tx.ChargeLimitSetAt = DateTime.UtcNow;
        tx.ChargeLimitReachedAt = null;   // a new / raised limit re-arms the check (a lower one stops on the next reading)
        await db.SaveChanges(ct);
        return ChargeLimitDto.From(tx.ChargeLimitKind, tx.ChargeLimitValue, tx.ChargeLimitSetAt, null);
    }
}

/// <summary>Owner / manager (ConnectControl): set or clear the limit of a session at their station.</summary>
public record SetMySessionChargeLimitCommand(int ChargingPointId, int TransactionId, string? Kind, decimal? Value) : IRequest<ChargeLimitDto?>;

public class SetMySessionChargeLimitCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetMySessionChargeLimitCommand, ChargeLimitDto?>
{
    public async Task<ChargeLimitDto?> Handle(SetMySessionChargeLimitCommand request, CancellationToken cancellationToken)
    {
        await ProviderAccessGuard.EnsureCanActForProviderAsync(db, currentUser, ProviderAccessGuard.ChargingPoint, request.ChargingPointId,
            cancellationToken, requiredPrivilege: Cable.Core.Constants.WorkerPrivileges.ConnectControl);
        var tx = await db.OcppTransactions.FirstOrDefaultAsync(t => t.Id == request.TransactionId
                     && t.ChargePoint.ChargingPointId == request.ChargingPointId, cancellationToken)
                 ?? throw new NotFoundException("cannot find session " + request.TransactionId + " at this station");
        return await ChargeLimitSetter.ApplyAsync(db, tx, request.Kind, request.Value, currentUser.UserId, cancellationToken);
    }
}

/// <summary>Admin: set or clear the limit of any running session.</summary>
public record SetOcppSessionChargeLimitCommand(int TransactionId, string? Kind, decimal? Value) : IRequest<ChargeLimitDto?>;

public class SetOcppSessionChargeLimitCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetOcppSessionChargeLimitCommand, ChargeLimitDto?>
{
    public async Task<ChargeLimitDto?> Handle(SetOcppSessionChargeLimitCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);
        var tx = await db.OcppTransactions.FirstOrDefaultAsync(t => t.Id == request.TransactionId, cancellationToken)
                 ?? throw new NotFoundException("cannot find session " + request.TransactionId);
        return await ChargeLimitSetter.ApplyAsync(db, tx, request.Kind, request.Value, currentUser.UserId, cancellationToken);
    }
}

/// <summary>Driver: set or clear the limit of their own running session.</summary>
public record SetMyOcppSessionChargeLimitCommand(int TransactionId, string? Kind, decimal? Value) : IRequest<ChargeLimitDto?>;

public class SetMyOcppSessionChargeLimitCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<SetMyOcppSessionChargeLimitCommand, ChargeLimitDto?>
{
    public async Task<ChargeLimitDto?> Handle(SetMyOcppSessionChargeLimitCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var tx = await db.OcppTransactions.FirstOrDefaultAsync(t => t.Id == request.TransactionId && t.StartedByUserId == userId, cancellationToken)
                 ?? throw new NotFoundException("cannot find your session " + request.TransactionId);
        return await ChargeLimitSetter.ApplyAsync(db, tx, request.Kind, request.Value, userId, cancellationToken);
    }
}
