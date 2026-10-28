using Application.ChargingPoints.Queries.GetChargingPointById;

namespace Application.UserComplaints.Queries.GetAllUserComplaints;

public record GetUserComplaintsDto(
    int Id,
    string Note,
    int Status,
    UserAccountSummary UserAccount,
    ChargingPointSummary ChargingPoint,
    DateTime? CreatedAt = null,
    DateTime? ModifiedAt = null,
    // Sprint 5 (§13.1): "problem with this session" complaints carry their session.
    /// <summary>General | ChargingSession.</summary>
    string Kind = "General",
    /// <summary>ChargingSession: WrongCost | StoppedEarly | NoEnergy | CableStuck | PlugBroken | Other.</summary>
    string? Reason = null,
    string? ImageFileName = null,
    /// <summary>The partner's / admin's answer to the driver.</summary>
    string? Reply = null,
    int? OcppTransactionId = null,
    /// <summary>ChargingSession: the session's charger, plug, times, energy and cost.</summary>
    Application.Ocpp.LiveCharging.ComplaintSessionDto? Session = null);

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
                ))
            // Filled after mapping by ComplaintSessionEnricher (one query for the whole list).
            .ForCtorParam("Session", opt => opt.MapFrom(_ => (Application.Ocpp.LiveCharging.ComplaintSessionDto?)null));

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