using Application.Users.Queries.GetAllUsers;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Users.Queries.GetUserById;

public record GetUserByIdRequest(int Id) : IRequest<GetUserByIdDto>;

public class GetUserByIdQueryHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetUserByIdRequest, GetUserByIdDto>
{
    public async Task<GetUserByIdDto> Handle(GetUserByIdRequest request, CancellationToken cancellationToken)
        => await applicationDbContext.UserAccounts
            .Include(x => x.Role)
            .AsNoTracking().Where(x => x.Id == request.Id).Select(x =>
                new GetUserByIdDto(x.Id, x.Name, x.Phone,
                    x.IsActive,
                    x.Email,
                    x.RegistrationProvider,
                    x.FirebaseUId,
                    x.Country,
                    x.City,
                    new RoleSummary(x.Role.Id,x.Role.Name)
                    ))
            .FirstOrDefaultAsync(cancellationToken) ?? throw new NotFoundException($"Can not find user {request.Id}");
}