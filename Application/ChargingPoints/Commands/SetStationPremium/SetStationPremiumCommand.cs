using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.SetStationPremium;

public record SetStationPremiumResult(int SubscriptionId, DateTime PaymentDate, DateTime ExpiresAt);

/// <summary>
/// Records a premium payment for a station: appends a row to the payment history,
/// syncs the denormalized dates on the charging point and marks it Premium.
/// Calling it again (renewal) appends a new history row and moves the dates forward.
/// </summary>
public record SetStationPremiumCommand(
    int ChargingPointId,
    DateTime PaymentDate,
    DateTime ExpiresAt,
    decimal? Amount,
    string? Note) : IRequest<SetStationPremiumResult>;

public class SetStationPremiumCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<SetStationPremiumCommand, SetStationPremiumResult>
{
    // StationType seed: 1 = Normal, 2 = Premium, 3 = Charge Point
    private const int PremiumStationTypeId = 2;

    public async Task<SetStationPremiumResult> Handle(SetStationPremiumCommand request,
        CancellationToken cancellationToken)
    {
        var chargingPoint = await applicationDbContext.ChargingPoints
                                .FirstOrDefaultAsync(x => x.Id == request.ChargingPointId && !x.IsDeleted,
                                    cancellationToken)
                            ?? throw new NotFoundException(
                                $"can not find charging point with id {request.ChargingPointId}");

        // 1. Append to the immutable payment history.
        var subscription = new StationPremiumSubscription
        {
            ChargingPointId = chargingPoint.Id,
            PaymentDate = request.PaymentDate,
            ExpiresAt = request.ExpiresAt,
            Amount = request.Amount,
            Note = request.Note
        };
        applicationDbContext.StationPremiumSubscriptions.Add(subscription);

        // 2. Sync the denormalized dates and mark the station Premium.
        chargingPoint.PremiumPaymentDate = request.PaymentDate;
        chargingPoint.PremiumExpiresAt = request.ExpiresAt;
        chargingPoint.StationTypeId = PremiumStationTypeId;

        await applicationDbContext.SaveChanges(cancellationToken);

        return new SetStationPremiumResult(subscription.Id, subscription.PaymentDate, subscription.ExpiresAt);
    }
}
