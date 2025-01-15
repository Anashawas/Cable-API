using Cable.Core.Exceptions;
using Cable.Security.Encryption.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Commands.ChangePassword;

public record ChangeMyPasswordCommand(string CurrentPassword, string NewPassword) : IRequest;



public class ChangeMyPasswordCommandHandler(IApplicationDbContext applicationDbContext , IPasswordHasher passwordHasher ,ICurrentUserService  currentUserService) : IRequestHandler<ChangeMyPasswordCommand>
{
    public async Task Handle(ChangeMyPasswordCommand request, CancellationToken cancellationToken)
    {
        var user = await applicationDbContext.UserAccounts.FirstOrDefaultAsync(x => x.Id == currentUserService.UserId && !x.IsDeleted, cancellationToken);

        if (user == null)
        {
            throw new NotFoundException();
        }


        user.Password = string.IsNullOrEmpty(request.NewPassword) ? null : passwordHasher.HashPassword(request.NewPassword);
        await applicationDbContext.SaveChanges(cancellationToken);


    }
}