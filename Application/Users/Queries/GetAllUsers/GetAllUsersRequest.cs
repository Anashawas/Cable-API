using Microsoft.EntityFrameworkCore;

namespace Application.Users.Queries.GetAllUsers;

public record GetAllUsersRequest() : IRequest<List<GetAllUsersDto>>;

public class GetAllUsersQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllUsersRequest, List<GetAllUsersDto>>
{
    public async Task<List<GetAllUsersDto>> Handle(GetAllUsersRequest request, CancellationToken cancellationToken)
        =>
            await applicationDbContext.UserAccounts.AsNoTracking().Include(x => x.Role)
                .Where(x => !x.IsDeleted).Select(x => new GetAllUsersDto(x.Id, x.Name, x.Phone, x.Name, x.Email,
                    x.IsPhoneVerified, new RoleSummary(x.Role.Id, x.Role.Name))).ToListAsync(cancellationToken);
}