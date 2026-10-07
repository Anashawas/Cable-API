using Application.Common.Security;
using Application.Subscriptions;
using Cable.Core.Constants;
using Cable.Core.Enums;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Commands.SetStationPremium;

public record SetStationPremiumResult(int SubscriptionId, DateTime PaymentDate, DateTime ExpiresAt);

/// <summary>
/// Legacy premium endpoint, kept for the admin web's existing form. Since the
/// payment-tracking work it is a thin adapter over <see cref="PaymentRecorder"/>:
/// the same subscription, payment, payer and receipt are produced as by
/// RecordPayment, with the differences the old contract forces —
/// an explicit expiry instead of a plan, no payer (the station owner is used),
/// and no method (recorded as Cash). Prefer POST /api/subscriptions/payments.
/// </summary>
public record SetStationPremiumCommand(
    int ChargingPointId,
    DateTime PaymentDate,
    DateTime ExpiresAt,
    decimal? Amount,
    string? Note) : IRequest<SetStationPremiumResult>;

public class SetStationPremiumCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    PaymentRecorder recorder)
    : IRequestHandler<SetStationPremiumCommand, SetStationPremiumResult>
{
    public async Task<SetStationPremiumResult> Handle(SetStationPremiumCommand request,
        CancellationToken cancellationToken)
    {
        // Previously unguarded: any signed-in account could mark any station premium.
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var ownerId = await applicationDbContext.ChargingPoints.AsNoTracking()
                          .Where(x => x.Id == request.ChargingPointId && !x.IsDeleted)
                          .Select(x => new { x.OwnerId })
                          .FirstOrDefaultAsync(cancellationToken)
                      ?? throw new NotFoundException($"can not find charging point with id {request.ChargingPointId}");

        // The old form has no payer field; the owner is the only defensible
        // guess, and a station without one gets an explicit placeholder rather
        // than a fabricated person.
        var payer = ownerId.OwnerId is int uid
            ? new PaymentRecorder.PayerInput(null, uid, null, null, false, null, null)
            : new PaymentRecorder.PayerInput(null, null, "Unknown (legacy premium)", null, false, null, null);

        var recorded = await recorder.RecordAsync(
            SubscriptionEntityTypes.StationPremium, request.ChargingPointId,
            planMonths: null, explicitPeriodEnd: JordanTime.ToUtc(request.ExpiresAt),
            amount: request.Amount ?? 0, currency: "JOD", method: PaymentMethod.Cash,
            paidDateUtc: JordanTime.ToUtc(request.PaymentDate),
            payer, request.Note, receiptImageFileName: null, cancellationToken);

        return new SetStationPremiumResult(recorded.Subscription.Id, recorded.Payment.PaidDate, recorded.Payment.PeriodEnd);
    }
}
