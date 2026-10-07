using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Ocpp.Queries.GetOcppRawMessages;

/// <summary>Admin support tool: the last N frames exchanged with one charger, newest first.</summary>
public record GetOcppRawMessagesRequest(
    int OcppChargePointId,
    int? Take = null,
    /// <summary>"in" | "out" | "sys"</summary>
    string? Direction = null,
    string? Action = null,
    /// <summary>Only rows with Id below this — for "load older".</summary>
    long? BeforeId = null) : IRequest<List<OcppRawMessageDto>>;

public class GetOcppRawMessagesRequestHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetOcppRawMessagesRequest, List<OcppRawMessageDto>>
{
    public async Task<List<OcppRawMessageDto>> Handle(GetOcppRawMessagesRequest request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(db, currentUser, cancellationToken);

        var chargePointId = await db.OcppChargePoints.AsNoTracking()
            .Where(c => c.Id == request.OcppChargePointId && !c.IsDeleted)
            .Select(c => c.ChargePointId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"cannot find OCPP charge point with id: {request.OcppChargePointId}");

        var query = db.OcppRawMessages.AsNoTracking().Where(m => m.ChargePointId == chargePointId);
        if (!string.IsNullOrWhiteSpace(request.Direction))
            query = query.Where(m => m.Direction == request.Direction.Trim().ToLower());
        if (!string.IsNullOrWhiteSpace(request.Action))
            query = query.Where(m => m.Action == request.Action.Trim());
        if (request.BeforeId is long before)
            query = query.Where(m => m.Id < before);

        return await query
            .OrderByDescending(m => m.Id)
            .Take(Math.Clamp(request.Take ?? 100, 1, 1000))
            .Select(m => new OcppRawMessageDto(m.Id, m.Direction.Trim(), m.MessageType, m.MessageId, m.Action, m.Payload, m.RemoteIp, m.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
