using Application.Common.Interfaces.Repositories;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Commands.UpdateRate;

public record UpdateRateCommand(int Id, int ChargingPointRate) : IRequest;

public class UpdateRateCommandHandler(IApplicationDbContext applicationDbContext, IRateRepository rateRepository)
    : IRequestHandler<UpdateRateCommand>
{
    public async Task Handle(UpdateRateCommand request, CancellationToken cancellationToken)
    {
        var rate = await applicationDbContext.Rates.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken) ??
                   throw new NotFoundException($"can find rate with id {request.Id}");

        rate.AVGChargingPointRate =
            await rateRepository.CalculateChargePointAverageRate(rate.ChargingPointId, request.ChargingPointRate,
                cancellationToken);

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}