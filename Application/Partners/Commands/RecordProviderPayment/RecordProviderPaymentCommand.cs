using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Commands.RecordProviderPayment;

public record RecordProviderPaymentCommand(
    string ProviderType,
    int ProviderId,
    decimal Amount,
    string? Note
) : IRequest;

public class RecordProviderPaymentCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<RecordProviderPaymentCommand>
{
    public async Task Handle(RecordProviderPaymentCommand request, CancellationToken cancellationToken)
    {
        var adminUserId = currentUserService.UserId
                          ?? throw new NotAuthorizedAccessException("User not authenticated");

        if (request.Amount <= 0)
            throw new DataValidationException("Amount", "Payment amount must be greater than zero");

        if (request.ProviderType == "ChargingPoint")
        {
            var cp = await applicationDbContext.ChargingPoints
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ChargingPoint with Id '{request.ProviderId}' not found");

            cp.LoyaltyCurrentBalance += request.Amount;
        }
        else if (request.ProviderType == "ServiceProvider")
        {
            var sp = await applicationDbContext.ServiceProviders
                         .FirstOrDefaultAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken)
                     ?? throw new NotFoundException($"ServiceProvider with Id '{request.ProviderId}' not found");

            sp.LoyaltyCurrentBalance += request.Amount;
        }
        else
        {
            throw new DataValidationException("ProviderType",
                "ProviderType must be 'ChargingPoint' or 'ServiceProvider'");
        }

        var payment = new ProviderPayment
        {
            ProviderType = request.ProviderType,
            ProviderId = request.ProviderId,
            Amount = request.Amount,
            Note = request.Note,
            RecordedByUserId = adminUserId
        };

        applicationDbContext.ProviderPayments.Add(payment);
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
