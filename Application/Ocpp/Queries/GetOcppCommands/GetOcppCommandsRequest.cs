using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetOcppCommands;

/// <summary>Admin: the commands sent to one charger, newest first — who, what, what it answered.</summary>
public record GetOcppCommandsRequest(int OcppChargePointId, int? Take = null) : IRequest<List<OcppCommandDto>>;

public class GetOcppCommandsRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetOcppCommandsRequest, List<OcppCommandDto>>
{
    public async Task<List<OcppCommandDto>> Handle(GetOcppCommandsRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var exists = await db.OcppChargePoints.AsNoTracking()
            .AnyAsync(c => c.Id == request.OcppChargePointId && !c.IsDeleted, cancellationToken);
        if (!exists)
            throw new NotFoundException($"cannot find OCPP charge point with id: {request.OcppChargePointId}");

        return await db.OcppCommands.AsNoTracking()
            .Where(c => c.OcppChargePointId == request.OcppChargePointId && !c.IsDeleted)
            .OrderByDescending(c => c.Id)
            .Take(Math.Clamp(request.Take ?? 30, 1, 500))
            .Select(c => new OcppCommandDto(
                c.Id, c.Action, c.RequestPayload, c.Status, c.ResultStatus, c.ResponsePayload,
                c.ErrorCode, c.ErrorDescription, c.DurationMs, c.CreatedBy,
                db.UserAccounts.Where(u => u.Id == c.CreatedBy).Select(u => u.Name).FirstOrDefault(),
                c.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
