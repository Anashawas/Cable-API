using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Queries.GetAllUserComplaints;

public record GetAllUserComplaintsRequest() : IRequest<List<GetUserComplaintsDto>>;

public class GetAllUserComplaintsQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllUserComplaintsRequest, List<GetUserComplaintsDto>>
{
    public async Task<List<GetUserComplaintsDto>> Handle(GetAllUserComplaintsRequest request,
        CancellationToken cancellationToken)
        => mapper.Map<List<UserComplaint>, List<GetUserComplaintsDto>>(
            await applicationDbContext.UserComplaints.AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.ChargingPoint)
                .ToListAsync(cancellationToken)
        );
}