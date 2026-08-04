using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Queries.GetTransactionDetail;

public record TransactionUserDto(int? UserId, string? UserName, string? Phone);

public record TransactionProviderDto(string? ProviderType, int? ProviderId, string? ProviderName);

public record AdminTransactionDetailDto(
    string ActivityType,
    int TransactionId,
    int Status,
    string StatusName,
    TransactionUserDto User,
    TransactionProviderDto Provider,
    string? Code,
    decimal? Amount,
    string? CurrencyCode,
    decimal? CommissionAmount,
    int Points,
    string? Note,
    int? PerformedByUserId,
    string? PerformedByUserName,
    DateTime CreatedAt,
    DateTime? CompletedAt
);

/// <summary>
/// Admin detail view of any single transaction — Offer, Partner or (reward)
/// Redemption — enriched with the acting user, the provider, and the staff/admin
/// actor. Not owner-gated.
/// </summary>
public record GetTransactionDetailRequest(string ActivityType, int Id) : IRequest<AdminTransactionDetailDto>;

public class GetTransactionDetailRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTransactionDetailRequest, AdminTransactionDetailDto>
{
    public async Task<AdminTransactionDetailDto> Handle(GetTransactionDetailRequest request,
        CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        return request.ActivityType switch
        {
            "Offer" => await GetOfferDetail(request.Id, cancellationToken),
            "Partner" => await GetPartnerDetail(request.Id, cancellationToken),
            "Redemption" => await GetRedemptionDetail(request.Id, cancellationToken),
            _ => throw new DataValidationException("ActivityType",
                "ActivityType must be one of: Offer, Partner, Redemption")
        };
    }

    private async Task<AdminTransactionDetailDto> GetOfferDetail(int id, CancellationToken ct)
    {
        var t = await applicationDbContext.OfferTransactions
                    .AsNoTracking()
                    .Include(x => x.User)
                    .Include(x => x.ConfirmedByUser)
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                ?? throw new NotFoundException($"can not find offer transaction with id {id}");

        return new AdminTransactionDetailDto(
            "Offer",
            t.Id,
            t.Status,
            ((OfferTransactionStatus)t.Status).ToString(),
            new TransactionUserDto(t.UserId, t.User?.Name, t.User?.Phone),
            new TransactionProviderDto(t.ProviderType, t.ProviderId,
                await ResolveProviderName(t.ProviderType, t.ProviderId, ct)),
            t.OfferCode,
            t.MonetaryValue,
            t.CurrencyCode,
            null,
            -t.PointsDeducted,
            null,
            t.ConfirmedByUserId,
            t.ConfirmedByUser?.Name,
            t.CreatedAt,
            t.CompletedAt);
    }

    private async Task<AdminTransactionDetailDto> GetPartnerDetail(int id, CancellationToken ct)
    {
        var t = await applicationDbContext.PartnerTransactions
                    .AsNoTracking()
                    .Include(x => x.User)
                    .Include(x => x.ConfirmedByUser)
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                ?? throw new NotFoundException($"can not find partner transaction with id {id}");

        return new AdminTransactionDetailDto(
            "Partner",
            t.Id,
            t.Status,
            ((PartnerTransactionStatus)t.Status).ToString(),
            new TransactionUserDto(t.UserId, t.User?.Name, t.User?.Phone),
            new TransactionProviderDto(t.ProviderType, t.ProviderId,
                await ResolveProviderName(t.ProviderType, t.ProviderId, ct)),
            t.TransactionCode,
            t.TransactionAmount,
            t.CurrencyCode,
            t.CommissionAmount,
            t.PointsAwarded ?? 0,
            null,
            t.ConfirmedByUserId,
            t.ConfirmedByUser?.Name,
            t.CreatedAt,
            t.CompletedAt);
    }

    private async Task<AdminTransactionDetailDto> GetRedemptionDetail(int id, CancellationToken ct)
    {
        var r = await applicationDbContext.UserRewardRedemptions
                    .AsNoTracking()
                    .Include(x => x.User)
                    .Include(x => x.Reward)
                    .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                ?? throw new NotFoundException($"can not find redemption with id {id}");

        // For redemptions the admin actor is whoever fulfilled/cancelled it.
        int? performedById = null;
        string? performedByName = null;
        if (r.ModifiedBy.HasValue && r.ModifiedBy.Value != r.UserId)
        {
            performedById = r.ModifiedBy.Value;
            performedByName = await applicationDbContext.UserAccounts
                .Where(x => x.Id == r.ModifiedBy.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct);
        }

        return new AdminTransactionDetailDto(
            "Redemption",
            r.Id,
            r.Status,
            ((RedemptionStatus)r.Status).ToString(),
            new TransactionUserDto(r.UserId, r.User?.Name, r.User?.Phone),
            new TransactionProviderDto(r.ProviderType, r.ProviderId,
                await ResolveProviderName(r.ProviderType, r.ProviderId, ct)),
            r.RedemptionCode,
            null,
            null,
            null,
            -r.PointsSpent,
            r.Reward?.Name,
            performedById,
            performedByName,
            r.CreatedAt,
            r.FulfilledAt);
    }

    private async Task<string?> ResolveProviderName(string? providerType, int? providerId, CancellationToken ct)
    {
        if (providerId is null or 0) return null;

        return providerType switch
        {
            "ChargingPoint" => await applicationDbContext.ChargingPoints
                .Where(x => x.Id == providerId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct),
            "ServiceProvider" => await applicationDbContext.ServiceProviders
                .Where(x => x.Id == providerId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(ct),
            _ => null
        };
    }
}
