using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.UpdateUser;

public record UpdateUserCommand(
    int Id,
    string Name,
    string UserName,
    int RoleId,
    string Phone,
    string? Email,
    bool IsActive) : IRequest;

public class UpdateUserCommandHandler(IApplicationDbContext applicationDbContext) : IRequestHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted,
            cancellationToken) ?? throw new NotFoundException($"cannot find user with id: {request.Id}");

        user.Name = request.Name;
        user.UserName = request.UserName;
        user.RoleId = request.RoleId;
        user.IsActive = request.IsActive;
        user.Phone = request.Phone;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}