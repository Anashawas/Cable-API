using Application.Common.Extensions;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
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

        // Check minimum transaction amount
        if (agreement.MinimumTransactionAmount.HasValue && request.TransactionAmount < agreement.MinimumTransactionAmount.Value)
            throw new DataValidationException("TransactionAmount",
                $"Transaction amount must be at least {agreement.MinimumTransactionAmount.Value:F3} {request.CurrencyCode}");

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

        // Generate unique transaction code before transaction scope
        var transactionCode = await GenerateUniqueCode(cancellationToken);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddSeconds(agreement.CodeExpirySeconds);

        // Wrap wallet operations in a transaction with row locking
        await using var dbTransaction = await applicationDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            decimal walletCoveredAmount = 0;
            decimal walletBalanceAfter = 0;

            if (agreement.ProviderType == "ChargingPoint")
            {
                var cp = await applicationDbContext.ChargingPoints
                    .FindWithLockAsync("ChargingPoint", agreement.ProviderId, cancellationToken);
                if (cp is { IsLoyaltyBlocked: true } &&
                    (!cp.LoyaltyBlockedUntil.HasValue || cp.LoyaltyBlockedUntil.Value >= DateTime.UtcNow))
                    throw new DataValidationException("Loyalty", "This provider is currently blocked from the loyalty system");

                if (cp != null)
                {
                    if (cp.WalletCreditLimit.HasValue)
                    {
                        var newBalance = cp.WalletBalance - commissionAmount;
                        if (newBalance < -cp.WalletCreditLimit.Value)
                            throw new DataValidationException("CreditLimit",
                                $"Provider credit limit reached. Available credit: {(cp.WalletBalance + cp.WalletCreditLimit.Value):F3} {request.CurrencyCode}");
                    }

                    walletCoveredAmount = Math.Min(Math.Max(cp.WalletBalance, 0), commissionAmount);
                    cp.WalletBalance -= commissionAmount;
                    walletBalanceAfter = cp.WalletBalance;
                }
            }
            else if (agreement.ProviderType == "ServiceProvider")
            {
                var sp = await applicationDbContext.ServiceProviders
                    .FindWithLockAsync("ServiceProvider", agreement.ProviderId, cancellationToken);
                if (sp is { IsLoyaltyBlocked: true } &&
                    (!sp.LoyaltyBlockedUntil.HasValue || sp.LoyaltyBlockedUntil.Value >= DateTime.UtcNow))
                    throw new DataValidationException("Loyalty", "This provider is currently blocked from the loyalty system");

                if (sp != null)
                {
                    if (sp.WalletCreditLimit.HasValue)
                    {
                        var newBalance = sp.WalletBalance - commissionAmount;
                        if (newBalance < -sp.WalletCreditLimit.Value)
                            throw new DataValidationException("CreditLimit",
                                $"Provider credit limit reached. Available credit: {(sp.WalletBalance + sp.WalletCreditLimit.Value):F3} {request.CurrencyCode}");
                    }

                    walletCoveredAmount = Math.Min(Math.Max(sp.WalletBalance, 0), commissionAmount);
                    sp.WalletBalance -= commissionAmount;
                    walletBalanceAfter = sp.WalletBalance;
                }
            }

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
                CodeExpiresAt = expiresAt,
                WalletCoveredAmount = walletCoveredAmount
            };

            applicationDbContext.PartnerTransactions.Add(transaction);
            await applicationDbContext.SaveChanges(cancellationToken);

            // Create wallet audit record (transaction.Id now available)
            if (commissionAmount > 0)
            {
                applicationDbContext.ProviderWalletTransactions.Add(new ProviderWalletTransaction
                {
                    ProviderType = agreement.ProviderType,
                    ProviderId = agreement.ProviderId,
                    TransactionType = (int)WalletTransactionType.CommissionDeduction,
                    Amount = -commissionAmount,
                    BalanceAfter = walletBalanceAfter,
                    ReferenceType = "PartnerTransaction",
                    ReferenceId = transaction.Id,
                    Note = $"Commission deducted for transaction #{transaction.Id} ({transactionCode})",
                    RecordedByUserId = staffUserId
                });
                await applicationDbContext.SaveChanges(cancellationToken);
            }

            await dbTransaction.CommitAsync(cancellationToken);

            return new InitiatePartnerTransactionResult(
                transaction.Id, transactionCode, expiresAt, commissionAmount, pointsToBeAwarded, request.TransactionAmount);
        }
        catch
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            throw;
        }
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
