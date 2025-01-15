namespace Application.ChargingPointTypes.Queries;

public record GetAllChargingPointTypesDto(int Id, string Name);


public class ChargingPointTypesDtoMapping : Profile
{
    public ChargingPointTypesDtoMapping()
    {
        CreateMap<ChargingPointType, GetAllChargingPointTypesDto>();
    }
} 