
using Application.Common.Interfaces.Repositories;

using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetChargingPointById;

public record GetChargingPointByIdRequest(int Id) : IRequest<GetChargingPointByIdDto>;

public class GetChargingPointByIdQuery(
    IChargingPointRepository chargingPointRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetChargingPointByIdRequest, GetChargingPointByIdDto>
{
    public async Task<GetChargingPointByIdDto> Handle(GetChargingPointByIdRequest request,
        CancellationToken cancellationToken)
   =>await chargingPointRepository.GetChargingPointById(request.Id, currentUserService.UserId, cancellationToken);
}