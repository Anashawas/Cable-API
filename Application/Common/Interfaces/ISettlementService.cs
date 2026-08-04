using Domain.Enitites;

namespace Application.Common.Interfaces;

public interface ISettlementService
{
    /// <summary>
    /// Upserts the settlement for a completed partner transaction.
    /// Does NOT call SaveChanges — caller is responsible.
    /// </summary>
    Task UpsertSettlementForPartnerTransactionAsync(
        PartnerTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the settlement for a completed offer transaction.
    /// Does NOT call SaveChanges — caller is responsible.
    /// </summary>
    Task UpsertSettlementForOfferTransactionAsync(
        OfferTransaction transaction,
        CancellationToken cancellationToken = default);
}
