using Application.Common.Interfaces;
using Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetOcppAlerts;

/// <summary>Admin: the alert job's findings — open ones by default, newest first; or one charger's history.</summary>
public record GetOcppAlertsRequest(bool OpenOnly = true, int? OcppChargePointId = null, int? Take = null) : IRequest<List<OcppAlertDto>>;

public class GetOcppAlertsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetOcppAlertsRequest, List<OcppAlertDto>>
{
    public async Task<List<OcppAlertDto>> Handle(GetOcppAlertsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var query = db.OcppAlerts.AsNoTracking().Where(a => !a.IsDeleted && !a.ChargePoint.IsDeleted);
        if (request.OpenOnly) query = query.Where(a => a.ResolvedAt == null);
        if (request.OcppChargePointId is int cp) query = query.Where(a => a.OcppChargePointId == cp);

        return await query
            .OrderByDescending(a => a.ResolvedAt == null)
            .ThenByDescending(a => a.NotifiedAt)
            .Take(Math.Clamp(request.Take ?? 100, 1, 500))
            .Select(a => new OcppAlertDto(
                a.Id, a.Type, a.OcppChargePointId, a.ChargePoint.ChargePointId, a.ChargePoint.DisplayName,
                a.ChargePoint.ChargingPointId, a.ChargePoint.ChargingPoint.Name,
                a.ConnectorId, a.OcppTransactionId, a.ConditionSince, a.NotifiedAt, a.ResolvedAt, a.Details, a.Recipients))
            .ToListAsync(cancellationToken);
    }
}
