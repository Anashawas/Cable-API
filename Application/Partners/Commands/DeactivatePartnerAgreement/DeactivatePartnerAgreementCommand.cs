using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Partners.Commands.DeactivatePartnerAgreement;

public record DeactivatePartnerAgreementCommand(int Id) : IRequest;

public class DeactivatePartnerAgreementCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeactivatePartnerAgreementCommand>
{
    public async Task Handle(DeactivatePartnerAgreementCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var agreement = await applicationDbContext.PartnerAgreements
                            .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                        ?? throw new NotFoundException($"Partner agreement with Id '{request.Id}' not found");

        agreement.IsActive = false;
        agreement.ModifiedAt = DateTime.UtcNow;
        agreement.ModifiedBy = userId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
