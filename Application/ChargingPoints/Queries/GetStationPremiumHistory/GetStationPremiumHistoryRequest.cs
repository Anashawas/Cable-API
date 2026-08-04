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

/// <summary>Premium payment history for a station, newest first.</summary>
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

        var history = await applicationDbContext.StationPremiumSubscriptions
            .AsNoTracking()
            .Where(x => x.ChargingPointId == request.ChargingPointId && !x.IsDeleted)
            .OrderByDescending(x => x.PaymentDate)
            .Select(x => new StationPremiumSubscriptionDto(
                x.Id, x.PaymentDate, x.ExpiresAt, x.Amount, x.Note, x.CreatedAt, x.CreatedBy))
            .ToListAsync(cancellationToken);

        return new StationPremiumHistoryDto(
            request.ChargingPointId,
            chargingPoint.PremiumPaymentDate,
            chargingPoint.PremiumExpiresAt,
            chargingPoint.PremiumExpiresAt.HasValue && chargingPoint.PremiumExpiresAt.Value > DateTime.UtcNow,
            history);
    }
}
