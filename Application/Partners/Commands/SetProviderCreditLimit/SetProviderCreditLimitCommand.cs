using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Commands.SetProviderCreditLimit;

public record SetProviderCreditLimitCommand(
    string ProviderType,
    int ProviderId,
    decimal? CreditLimit
) : IRequest;

public class SetProviderCreditLimitCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<SetProviderCreditLimitCommand>
{
    public async Task Handle(SetProviderCreditLimitCommand request, CancellationToken cancellationToken)
    {
        _ = currentUserService.UserId
            ?? throw new NotAuthorizedAccessException("User not authenticated");

        if (request.CreditLimit is < 0)
            throw new DataValidationException("CreditLimit", "Credit limit must be a positive value or null for unlimited");

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ChargingPoint with Id '{request.ProviderId}' not found");

            cp.LoyaltyCreditLimit = request.CreditLimit;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ServiceProvider with Id '{request.ProviderId}' not found");

            sp.LoyaltyCreditLimit = request.CreditLimit;
        }
        else
        {
            throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
