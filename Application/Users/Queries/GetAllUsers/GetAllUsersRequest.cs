using Microsoft.EntityFrameworkCore;

namespace Application.Users.Queries.GetAllUsers;

public record GetAllUsersRequest() : IRequest<List<GetAllUsersDto>>;

public class GetAllUsersQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllUsersRequest, List<GetAllUsersDto>>
{
    public async Task<List<GetAllUsersDto>> Handle(GetAllUsersRequest request, CancellationToken cancellationToken)
        =>
            await applicationDbContext.UserAccounts.AsNoTracking()
                .Where(x => !x.IsDeleted)
                .Select(x => new GetAllUsersDto(
                    x.Id, x.Name, x.Phone, x.Name, x.Email,
                    x.IsPhoneVerified,
                    x.CreatedAt,
                    new RoleSummary(x.Role.Id, x.Role.Name),
                    x.UserCars.Select(uc => new UserCarSummaryDto(
                        uc.Id,
                        uc.CarModel.CarType.Name,
                        uc.CarModel.Name,
                        uc.PlugType.Name,
                        uc.CreatedAt
                    )).ToList()
                )).ToListAsync(cancellationToken);
}
