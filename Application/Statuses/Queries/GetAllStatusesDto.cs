namespace Application.Statuses.Queries;

public record GetAllStatusesDto(int Id, string Name);


public class StatusDtoMapping : Profile
{
    public StatusDtoMapping()
    {
        CreateMap<Status, GetAllStatusesDto>();
    }
}