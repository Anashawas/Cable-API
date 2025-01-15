namespace Application.Users.Queries.GetAllUsers;

public record GetAllUsersDto();

/// <summary>
/// The summary of the role
/// </summary>
/// <param name="Id">The id of the role</param>
/// <param name="Name">The name of the role</param>
public record RoleSummary(int Id, string Name);



public class GetAllUsersDtoMapper : Profile
{
    public GetAllUsersDtoMapper()
    {
        CreateMap<UserAccount, GetAllUsersDto>()
            .ForCtorParam("Role", opt => opt.MapFrom((userAccount, context)
                => context.Mapper.Map<Role, RoleSummary>
                (
                    userAccount.Role
                )
            ))
            
            ;
    }
}


public class RoleSummaryMapper : Profile
{
    public RoleSummaryMapper()
    {
        CreateMap<Role, RoleSummary>();
    }
}