using Microsoft.EntityFrameworkCore;

namespace Application.Users.Queries.GetAllUsers;

public record GetAllUsersRequest(bool? IncludeDeleted = false, bool? DeletedOnly = false) : IRequest<List<GetAllUsersDto>>;

public class GetAllUsersQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllUsersRequest, List<GetAllUsersDto>>
{
    public async Task<List<GetAllUsersDto>> Handle(GetAllUsersRequest request, CancellationToken cancellationToken)
    {
        var query = applicationDbContext.UserAccounts.AsNoTracking().AsQueryable();

        if (request.DeletedOnly == true)
            query = query.Where(x => x.IsDeleted);
        else if (request.IncludeDeleted != true)
            query = query.Where(x => !x.IsDeleted);

        return await query
            .Select(x => new GetAllUsersDto(
                x.Id, x.Name, x.Phone, x.Name, x.Email,
                x.City,
                x.IsPhoneVerified,
                x.HasReadUpdateNotes,
                x.IsDeleted,
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
}
