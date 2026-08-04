using System.Text.RegularExpressions;
using Application.Common.Models;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Queries.GetWalletHistory;

public record WalletTransactionDto(
    int Id,
    string ProviderType,
    int ProviderId,
    int TransactionType,
    decimal Amount,
    decimal BalanceAfter,
    string? ReferenceType,
    int? ReferenceId,
    string? Note,
    string? RecordedByUserName,
    DateTime CreatedAt,
    int? RelatedUserId = null,
    string? RelatedUserName = null,
    List<int>? RelatedTransactionIds = null
);

public record GetWalletHistoryRequest(
    string ProviderType,
    int ProviderId,
    int? Page = null,
    int? PageSize = null
) : IRequest<PagedResult<WalletTransactionDto>>;

public class GetWalletHistoryRequestHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetWalletHistoryRequest, PagedResult<WalletTransactionDto>>
{
    // Legacy batch-refund rows embed the ids in the note: "... (IDs: 4, 5)".
    private static readonly Regex LegacyIdsRegex = new(@"IDs?:\s*([\d,\s]+)", RegexOptions.Compiled);

    public async Task<PagedResult<WalletTransactionDto>> Handle(GetWalletHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var paged = await applicationDbContext.ProviderWalletTransactions
            .AsNoTracking()
            .Include(x => x.RecordedByUser)
            .Where(x => x.ProviderType == request.ProviderType
                         && x.ProviderId == request.ProviderId
                         && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ToOptionallyPaginatedAsync(request.Page, request.PageSize, cancellationToken: cancellationToken);
        var transactions = paged.Items;

        // C8 — resolve the underlying customer transaction(s) for each wallet row.
        var relatedIdsPerRow = new Dictionary<int, List<int>>();
        var partnerIds = new HashSet<int>();
        var offerIds = new HashSet<int>();

        foreach (var t in transactions)
        {
            List<int> ids = [];
            switch (t.ReferenceType)
            {
                case "PartnerTransaction" when t.ReferenceId.HasValue:
                    ids.Add(t.ReferenceId.Value);
                    partnerIds.Add(t.ReferenceId.Value);
                    break;
                case "OfferTransaction" when t.ReferenceId.HasValue:
                    ids.Add(t.ReferenceId.Value);
                    offerIds.Add(t.ReferenceId.Value);
                    break;
                case "ExpiredPartnerTransactions" when t.Note != null:
                    var m = LegacyIdsRegex.Match(t.Note);
                    if (m.Success)
                    {
                        ids = m.Groups[1].Value
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(s => int.TryParse(s, out _))
                            .Select(int.Parse)
                            .ToList();
                        foreach (var id in ids) partnerIds.Add(id);
                    }
                    break;
            }

            if (ids.Count > 0)
                relatedIdsPerRow[t.Id] = ids;
        }

        var partnerUsers = partnerIds.Count > 0
            ? await applicationDbContext.PartnerTransactions.AsNoTracking()
                .Where(x => partnerIds.Contains(x.Id))
                .Select(x => new { x.Id, x.UserId, UserName = x.User != null ? x.User.Name : null })
                .ToDictionaryAsync(x => x.Id, cancellationToken)
            : [];

        var offerUsers = offerIds.Count > 0
            ? await applicationDbContext.OfferTransactions.AsNoTracking()
                .Where(x => offerIds.Contains(x.Id))
                .Select(x => new { x.Id, x.UserId, UserName = x.User != null ? x.User.Name : null })
                .ToDictionaryAsync(x => x.Id, cancellationToken)
            : [];

        return paged.As(transactions.Select(x =>
        {
            int? relatedUserId = null;
            string? relatedUserName = null;
            List<int>? relatedIds = null;

            if (relatedIdsPerRow.TryGetValue(x.Id, out var ids))
            {
                relatedIds = ids;
                // A single underlying transaction identifies the customer;
                // multi-transaction batches may span users, so no single chip.
                if (ids.Count == 1)
                {
                    if (x.ReferenceType == "OfferTransaction" && offerUsers.TryGetValue(ids[0], out var ou))
                    {
                        relatedUserId = ou.UserId;
                        relatedUserName = ou.UserName;
                    }
                    else if (partnerUsers.TryGetValue(ids[0], out var pu))
                    {
                        relatedUserId = pu.UserId;
                        relatedUserName = pu.UserName;
                    }
                }
            }

            return new WalletTransactionDto(
                x.Id, x.ProviderType, x.ProviderId,
                x.TransactionType, x.Amount, x.BalanceAfter,
                x.ReferenceType, x.ReferenceId, x.Note,
                x.RecordedByUser?.Name, x.CreatedAt,
                relatedUserId, relatedUserName, relatedIds);
        }).ToList());
    }
}
