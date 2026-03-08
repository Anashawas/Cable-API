using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.SystemVersions.Queries.CheckSystemVerison;

public record CheckSystemVersionRequest(string Platform, string Version) : IRequest<CheckSystemVersionDto>;



public record CheckSystemVersionDto(bool latestVersion,bool ForceUpdate );

public class CheckSystemVersionQueryHandler(IApplicationDbContext applicationDbContext) : IRequestHandler<CheckSystemVersionRequest, CheckSystemVersionDto>
{
    public async Task<CheckSystemVersionDto> Handle(CheckSystemVersionRequest request, CancellationToken cancellationToken)
    {
       var systemVersion = await applicationDbContext.SystemVersions.FirstOrDefaultAsync(x=>x.Platform == request.Platform,cancellationToken)
                            ?? throw new NotFoundException($"Can not find platform {request.Platform}");
       
       
       return new CheckSystemVersionDto(systemVersion.Version == request.Version,systemVersion.ForceUpdate );
    }
}