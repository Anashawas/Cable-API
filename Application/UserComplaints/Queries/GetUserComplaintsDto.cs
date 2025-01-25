using Application.ChargingPoints.Queries.GetChargingPointById;

namespace Application.UserComplaints.Queries.GetAllUserComplaints;

public record GetUserComplaintsDto(
    int Id,
    string Note,
    UserAccountSummary UserAccount,
    ChargingPointSummary ChargingPoint);

public record ChargingPointSummary(int Id, string Name);

public class GetAllUserComplaintsDtoMapping : Profile
{
    public GetAllUserComplaintsDtoMapping()
    {
        CreateMap<UserComplaint, GetUserComplaintsDto>()
            .ForCtorParam("UserAccount", opt =>
                opt.MapFrom((userAccount, context) =>
                    context.Mapper.Map<UserAccountSummary>(userAccount.User)
                ))
            .ForCtorParam("ChargingPoint", opt =>
                opt.MapFrom((chargingPoint, context) =>
                    context.Mapper.Map<ChargingPointSummary>(chargingPoint.ChargingPoint)
                ));

    }
}

public class UserComplaintsSummariesMapping : Profile
{
    public UserComplaintsSummariesMapping()
    {
        CreateMap<UserAccount, UserAccountSummary>();
        CreateMap<ChargingPoint, ChargingPointSummary>();
    }
}