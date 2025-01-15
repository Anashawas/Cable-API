namespace Application.PlugTypes.Queries;

public record GetAllPlugTypesDto(int Id, string? Name, string SerialNumber);


public class PlugTypeDtoMapping : Profile
{
    public PlugTypeDtoMapping()
    {
        CreateMap<PlugType, GetAllPlugTypesDto>();
    }
}