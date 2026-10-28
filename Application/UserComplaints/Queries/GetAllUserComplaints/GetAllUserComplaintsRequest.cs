using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Queries.GetAllUserComplaints;

/// <param name="Kind">Optional filter: General | ChargingSession ("charging complaints").</param>
public record GetAllUserComplaintsRequest(string? Kind = null) : IRequest<List<GetUserComplaintsDto>>;

public class GetAllUserComplaintsQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllUserComplaintsRequest, List<GetUserComplaintsDto>>
{
    public async Task<List<GetUserComplaintsDto>> Handle(GetAllUserComplaintsRequest request,
        CancellationToken cancellationToken)
        => await UserComplaintSessions.WithSessionsAsync(applicationDbContext, mapper.Map<List<UserComplaint>, List<GetUserComplaintsDto>>(
            await applicationDbContext.UserComplaints.AsNoTracking()
                .Include(x => x.User)
                .Include(x => x.ChargingPoint)
                .Where(x => string.IsNullOrEmpty(request.Kind) || x.Kind == request.Kind)
                .ToListAsync(cancellationToken)
        ), cancellationToken);
}