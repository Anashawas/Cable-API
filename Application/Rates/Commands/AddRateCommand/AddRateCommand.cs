using Application.Common.Interfaces.Repositories;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Commands.AddRateCommand;

public record AddRateCommand(int ChargingPointId, int ChargingPointRate) : IRequest<int>;

public class AddRateCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IRateRepository rateRepository)
    : IRequestHandler<AddRateCommand, int>
{
    public async Task<int> Handle(AddRateCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.AsNoTracking()
                       .FirstOrDefaultAsync(x => !x.IsDeleted && x.Id == currentUserService.UserId, cancellationToken)
                   ?? throw new NotFoundException($"can not find user with id {currentUserService.UserId}");
        var rate = new Rate
        {
            UserId = user.Id,
            ChargingPointId = request.ChargingPointId,
            ChargingPointRate = request.ChargingPointRate,
            AVGChargingPointRate =
                await rateRepository.CalculateChargePointAverageRate(request.ChargingPointId, request.ChargingPointRate, cancellationToken)
        };

        applicationDbContext.Rates.Add(rate);
        await applicationDbContext.SaveChanges(cancellationToken);

        return rate.Id;
    }
}