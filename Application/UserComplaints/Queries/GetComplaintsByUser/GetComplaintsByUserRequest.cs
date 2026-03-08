using Application.Common.Interfaces;
using Application.UserComplaints.Queries.GetAllUserComplaints;
using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Queries.GetComplaintsByUser;

public record GetComplaintsByUserRequest : IRequest<List<GetUserComplaintsDto>>;

public class GetComplaintsByUserRequestHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService,
    IMapper mapper)
    : IRequestHandler<GetComplaintsByUserRequest, List<GetUserComplaintsDto>>
{
    public async Task<List<GetUserComplaintsDto>> Handle(GetComplaintsByUserRequest request, CancellationToken cancellationToken)
    {
        return mapper.Map<List<UserComplaint>, List<GetUserComplaintsDto>>(
            await applicationDbContext.UserComplaints.AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.ChargingPoint)
                .Where(x => x.UserId == currentUserService.UserId && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken)
        );
    }
}
