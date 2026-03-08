using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Commands.InitiatePartnerTransaction;

public record InitiatePartnerTransactionResult(
    int Id,
    string TransactionCode,
    DateTime ExpiresAt,
    decimal CommissionAmount,
    int PointsToBeAwarded,
    decimal TransactionAmount
);

public record InitiatePartnerTransactionCommand(
    int PartnerAgreementId,
    decimal TransactionAmount,
    string CurrencyCode
) : IRequest<InitiatePartnerTransactionResult>;

public class InitiatePartnerTransactionCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<InitiatePartnerTransactionCommand, InitiatePartnerTransactionResult>
{
    public async Task<InitiatePartnerTransactionResult> Handle(InitiatePartnerTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var staffUserId = currentUserService.UserId
                          ?? throw new NotAuthorizedAccessException("User not authenticated");

        var agreement = await applicationDbContext.PartnerAgreements
                            .Include(x => x.ConversionRate)
                            .FirstOrDefaultAsync(x => x.Id == request.PartnerAgreementId
                                                       && !x.IsDeleted
                                                       && x.IsActive,
                                cancellationToken)
                        ?? throw new NotFoundException($"Active partner agreement with id {request.PartnerAgreementId} not found");

        // Get conversion rate (agreement-specific or default)
        double conversionRate;
        if (agreement.ConversionRate != null)
        {
            conversionRate = agreement.ConversionRate.PointsPerUnit;
        }
        else
        {
            var defaultRate = await applicationDbContext.PointsConversionRates
                .FirstOrDefaultAsync(x => x.IsDefault && x.IsActive && !x.IsDeleted, cancellationToken);
            conversionRate = defaultRate?.PointsPerUnit ?? 0;
        }

        // Calculate amounts upfront
        var commissionAmount = request.TransactionAmount * (decimal)(agreement.CommissionPercentage / 100.0);
        var pointsEligibleAmount = request.TransactionAmount * (decimal)(agreement.PointsRewardPercentage / 100.0);
        var pointsToBeAwarded = (int)Math.Floor((double)pointsEligibleAmount * conversionRate);

        // Check if provider is blocked from loyalty + check credit limit + reserve commission
        if (agreement.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                .FirstOrDefaultAsync(x => x.Id == agreement.ProviderId && !x.IsDeleted, cancellationToken);
            if (cp is { IsLoyaltyBlocked: true } &&
                (!cp.LoyaltyBlockedUntil.HasValue || cp.LoyaltyBlockedUntil.Value >= DateTime.UtcNow))
                throw new DataValidationException("Loyalty", "This provider is currently blocked from the loyalty system");

            if (cp != null)
            {
                if (cp.LoyaltyCreditLimit.HasValue)
                {
                    var newBalance = cp.LoyaltyCurrentBalance - commissionAmount;
                    if (newBalance < -cp.LoyaltyCreditLimit.Value)
                        throw new DataValidationException("CreditLimit",
                            $"Provider credit limit reached. Available credit: {(cp.LoyaltyCurrentBalance + cp.LoyaltyCreditLimit.Value):F3} {request.CurrencyCode}");
                }
                cp.LoyaltyCurrentBalance -= commissionAmount;
            }
        }
        else if (agreement.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                .FirstOrDefaultAsync(x => x.Id == agreement.ProviderId && !x.IsDeleted, cancellationToken);
            if (sp is { IsLoyaltyBlocked: true } &&
                (!sp.LoyaltyBlockedUntil.HasValue || sp.LoyaltyBlockedUntil.Value >= DateTime.UtcNow))
                throw new DataValidationException("Loyalty", "This provider is currently blocked from the loyalty system");

            if (sp != null)
            {
                if (sp.LoyaltyCreditLimit.HasValue)
                {
                    var newBalance = sp.LoyaltyCurrentBalance - commissionAmount;
                    if (newBalance < -sp.LoyaltyCreditLimit.Value)
                        throw new DataValidationException("CreditLimit",
                            $"Provider credit limit reached. Available credit: {(sp.LoyaltyCurrentBalance + sp.LoyaltyCreditLimit.Value):F3} {request.CurrencyCode}");
                }
                sp.LoyaltyCurrentBalance -= commissionAmount;
            }
        }

        // Generate unique transaction code
        var transactionCode = await GenerateUniqueCode(cancellationToken);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(agreement.CodeExpiryMinutes);

        var transaction = new PartnerTransaction
        {
            PartnerAgreementId = agreement.Id,
            TransactionCode = transactionCode,
            Status = (int)PartnerTransactionStatus.Initiated,
            ProviderType = agreement.ProviderType,
            ProviderId = agreement.ProviderId,
            TransactionAmount = request.TransactionAmount,
            CurrencyCode = request.CurrencyCode,
            CommissionPercentage = agreement.CommissionPercentage,
            CommissionAmount = commissionAmount,
            PointsRewardPercentage = agreement.PointsRewardPercentage,
            PointsConversionRate = conversionRate,
            PointsEligibleAmount = pointsEligibleAmount,
            PointsAwarded = pointsToBeAwarded,
            ConfirmedByUserId = staffUserId,
            CodeExpiresAt = expiresAt
        };

        applicationDbContext.PartnerTransactions.Add(transaction);
        await applicationDbContext.SaveChanges(cancellationToken);

        return new InitiatePartnerTransactionResult(
            transaction.Id, transactionCode, expiresAt, commissionAmount, pointsToBeAwarded, request.TransactionAmount);
    }

    private async Task<string> GenerateUniqueCode(CancellationToken cancellationToken)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        string code;

        do
        {
            var randomPart = new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
            code = $"PTR-{randomPart}";
        } while (await applicationDbContext.PartnerTransactions
                     .AnyAsync(x => x.TransactionCode == code, cancellationToken));

        return code;
    }
}
