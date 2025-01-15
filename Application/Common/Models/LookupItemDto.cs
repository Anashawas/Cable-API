using AutoMapper;
using Domain.Enitites;

namespace Application.Common.Models;

/// <summary>
/// Data Lookup
/// </summary>
/// <param name="Id">The id of the record</param>
/// <param name="Name">The display name of the record</param>
public record LookupItemDto(int Id, string Name);

public record CodeLookupItemDto(int Id, string Name, string Code);

public class CodeLookupItemDtoMapping : Profile
{
    public CodeLookupItemDtoMapping()
    {
        CreateMap<Privilege, LookupItemDto>(MemberList.None);
    }
}