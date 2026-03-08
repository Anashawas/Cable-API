using Application.Partners.Queries.GetMyPartnerTransactions;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Queries.GetMyPartnerTransactionById;

public record GetMyPartnerTransactionByIdRequest(int Id) : IRequest<PartnerTransactionDto>;

public class GetMyPartnerTransactionByIdRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyPartnerTransactionByIdRequest, PartnerTransactionDto>
{
    public async Task<PartnerTransactionDto> Handle(GetMyPartnerTransactionByIdRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var t = await applicationDbContext.PartnerTransactions
                    .FirstOrDefaultAsync(x => x.Id == request.Id
                                              && x.UserId == userId
                                              && !x.IsDeleted, cancellationToken)
                ?? throw new NotFoundException($"Partner transaction with Id '{request.Id}' not found");

        string? providerName = null;
        if (t.ProviderType == "ChargingPoint")
        {
            providerName = await applicationDbContext.ChargingPoints
                .Where(x => x.Id == t.ProviderId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else if (t.ProviderType == "ServiceProvider")
        {
            providerName = await applicationDbContext.ServiceProviders
                .Where(x => x.Id == t.ProviderId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new PartnerTransactionDto(
            t.Id, t.PartnerAgreementId, providerName, t.TransactionCode,
            t.Status, t.ProviderType, t.ProviderId,
            t.TransactionAmount, t.CurrencyCode, t.CommissionPercentage,
            t.CommissionAmount, t.PointsAwarded,
            t.CodeExpiresAt, t.CompletedAt, t.CreatedAt);
    }
}
