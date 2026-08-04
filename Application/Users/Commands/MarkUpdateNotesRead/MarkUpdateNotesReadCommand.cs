using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.MarkUpdateNotesRead;

public record MarkUpdateNotesReadCommand() : IRequest;

public class MarkUpdateNotesReadCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<MarkUpdateNotesReadCommand>
{
    public async Task Handle(MarkUpdateNotesReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
                     ?? throw new NotAuthorizedAccessException("User not authenticated");

        var user = await applicationDbContext.UserAccounts
                       .FirstOrDefaultAsync(x => x.Id == userId && !x.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException($"User with id {userId} not found");

        user.HasReadUpdateNotes = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
