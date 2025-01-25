using Microsoft.EntityFrameworkCore;

namespace Application.Users.Queries.GetAllUsers;
public record GetAllUsersRequest() : IRequest<List<GetAllUsersDto>>;

public class GetAllUsersQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllUsersRequest, List<GetAllUsersDto>>
{
    public async Task<List<GetAllUsersDto>> Handle(GetAllUsersRequest request, CancellationToken cancellationToken)
        => mapper.Map<List<UserAccount>, List<GetAllUsersDto>>(
            await applicationDbContext.UserAccounts.AsNoTracking().Include(x => x.Role)
                .Where(x => !x.IsDeleted).ToListAsync(cancellationToken)
        );
}