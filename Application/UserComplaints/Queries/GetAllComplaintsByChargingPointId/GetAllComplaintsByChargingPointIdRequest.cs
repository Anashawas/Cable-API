using Application.UserComplaints.Queries.GetAllUserComplaints;
using Microsoft.EntityFrameworkCore;

namespace Application.UserComplaints.Queries.GetAllComplaintsByChargingPointId;

public record GetAllComplaintsByChargingPointIdRequest(int ChargingPointId):IRequest<List<GetUserComplaintsDto>>;

public class GetAllComplaintsByChargingPointIdQueryHandler(IApplicationDbContext applicationDbContext, IMapper mapper)
    : IRequestHandler<GetAllComplaintsByChargingPointIdRequest,List<GetUserComplaintsDto>>
{
    public async Task<List<GetUserComplaintsDto>> Handle(GetAllComplaintsByChargingPointIdRequest request, CancellationToken cancellationToken)
    {
        return mapper.Map<List<UserComplaint>, List<GetUserComplaintsDto>>(
            await applicationDbContext.UserComplaints.AsNoTracking()
                .Where(x => x.ChargingPointId == request.ChargingPointId && !x.IsDeleted)
                .ToListAsync(cancellationToken)
        );
    }
}