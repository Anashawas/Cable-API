using Application.Settings;
using Application.Subscriptions;
using Cable.Core.Constants;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetStationPremiumHistory;

public record StationPremiumSubscriptionDto(
    int Id,
    DateTime PaymentDate,
    DateTime ExpiresAt,
    decimal? Amount,
    string? Note,
    DateTime CreatedAt,
    int? CreatedBy);

public record StationPremiumHistoryDto(
    int ChargingPointId,
    DateTime? CurrentPaymentDate,
    DateTime? CurrentExpiresAt,
    bool IsPremiumActive,
    List<StationPremiumSubscriptionDto> History);

/// <summary>
/// Premium payment history for a station, newest first. Same response shape as
/// before the payment-tracking work; it now reads the unified Subscription /
/// Payment tables, and "active" honours the grace rule and the admin switch.
/// </summary>
public record GetStationPremiumHistoryRequest(int ChargingPointId) : IRequest<StationPremiumHistoryDto>;

public class GetStationPremiumHistoryRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetStationPremiumHistoryRequest, StationPremiumHistoryDto>
{
    public async Task<StationPremiumHistoryDto> Handle(GetStationPremiumHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var chargingPoint = await applicationDbContext.ChargingPoints
                                .AsNoTracking()
                                .Where(x => x.Id == request.ChargingPointId && !x.IsDeleted)
                                .Select(x => new { x.PremiumPaymentDate, x.PremiumExpiresAt })
                                .FirstOrDefaultAsync(cancellationToken)
                            ?? throw new NotFoundException(
                                $"can not find charging point with id {request.ChargingPointId}");

        var subscription = await applicationDbContext.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EntityType == SubscriptionEntityTypes.StationPremium
                                      && s.EntityId == request.ChargingPointId && !s.IsDeleted, cancellationToken);

        var history = subscription == null
            ? []
            : await applicationDbContext.Payments.AsNoTracking()
                .Where(p => p.SubscriptionId == subscription.Id && !p.IsDeleted && !p.IsVoid)
                .OrderByDescending(p => p.PaidDate)
                .Select(p => new StationPremiumSubscriptionDto(
                    p.Id, p.PaidDate, p.PeriodEnd, p.Amount, p.Note, p.CreatedAt, p.CreatedBy))
                .ToListAsync(cancellationToken);

        var isActive = false;
        if (subscription != null)
        {
            var grace = await AppSettingsProvider.GetSubscriptionGraceAsync(applicationDbContext, cancellationToken);
            isActive = SubscriptionPeriodCalculator.IsOn(subscription, grace, DateTime.UtcNow);
        }

        return new StationPremiumHistoryDto(
            request.ChargingPointId,
            chargingPoint.PremiumPaymentDate,
            chargingPoint.PremiumExpiresAt,
            isActive,
            history);
    }
}
