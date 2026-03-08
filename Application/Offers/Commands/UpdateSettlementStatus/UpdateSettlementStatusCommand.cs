using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Offers.Commands.UpdateSettlementStatus;

public record UpdateSettlementStatusCommand(
    int Id,
    int Status,
    decimal? PaidAmount,
    string? Note
) : IRequest;

public class UpdateSettlementStatusCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateSettlementStatusCommand>
{
    public async Task Handle(UpdateSettlementStatusCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var settlement = await applicationDbContext.ProviderSettlements
                             .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken)
                         ?? throw new NotFoundException($"Settlement with id {request.Id} not found");

        settlement.SettlementStatus = request.Status;
        settlement.AdminNote = request.Note;

        if (request.Status == (int)SettlementStatus.Invoiced)
            settlement.InvoicedAt = DateTime.UtcNow;

        if (request.Status == (int)SettlementStatus.Paid)
        {
            settlement.PaidAt = DateTime.UtcNow;
            settlement.PaidAmount = request.PaidAmount;
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
