using AutoMapper;
using Domain.Enitites;

namespace Application.Common.Models;

/// <summary>
/// The privilege details
/// </summary>
/// <param name="Code">The code of the privleges</param>
public record PrivilegeInfo(string Code);

/// <summary>
/// The privilege details
/// </summary>
/// <param name="Id">The id of the privilege</param>
/// <param name="Name">The name of the privilege</param>
public record RolePrivilegeInfo(int Id, String Name);

public class RolePrivilegeInfoMapper : Profile
{
    public RolePrivilegeInfoMapper()
    {
        CreateMap<Privilege, RolePrivilegeInfo>(MemberList.None);
    }
}