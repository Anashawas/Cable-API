using System.Security.Claims;
using Application.Common.Interfaces;
using Cable.Core.Exceptions;
using Cable.Core.Extenstions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Cable.Identity;

public class CurrentUserService( IHttpContextAccessor contextAccessor)
    : ICurrentUserService
{
    public int? UserId => contextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier).AsInt();

    public string Token => contextAccessor.HttpContext.Request.Headers[HeaderNames.Authorization].ToString()
        ?.Replace("Bearer", "", StringComparison.InvariantCultureIgnoreCase)?.Trim();
    

}