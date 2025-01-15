using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.DeleteUser;

public record DeleteUserCommand(int Id) : IRequest;




public class DeleteUserCommandHandler(IApplicationDbContext applicationDbContext) : IRequestHandler<DeleteUserCommand>
{
    public async Task Handle(DeleteUserCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, cancellationToken: cancellationToken);

        if (user == null)
        {
            throw new NotFoundException();
        }

        user.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);

        
    }
}
