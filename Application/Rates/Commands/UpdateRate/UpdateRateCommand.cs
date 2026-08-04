using Application.Common.Interfaces.Repositories;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Commands.UpdateRate;

public record UpdateRateCommand(int Id, int ChargingPointRate, string? Comment = null) : IRequest;

public class UpdateRateCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IRateRepository rateRepository)
    : IRequestHandler<UpdateRateCommand>
{
    public async Task Handle(UpdateRateCommand request, CancellationToken cancellationToken)
    {
        var rate = await applicationDbContext.Rates
                       .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken) ??
                   throw new NotFoundException($"can find rate with id {request.Id}");

        // F2 security fix: only the review's author (or an admin) may edit it —
        // previously ANY authenticated user could rewrite any review by id.
        if (rate.UserId != currentUserService.UserId
            && !await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken))
            throw new ForbiddenAccessException("You can only edit your own review.");

        rate.ChargingPointRate = request.ChargingPointRate;
        rate.Comment = request.Comment;
        rate.AVGChargingPointRate =
            await rateRepository.CalculateChargePointAverageRate(rate.ChargingPointId, request.ChargingPointRate,
                cancellationToken);

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}