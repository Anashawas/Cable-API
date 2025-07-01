using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.SystemVersions.Queries.CheckSystemVerison;

public record CheckSystemVersionRequest(string Platform, string Version) : IRequest<bool>;



public class CheckSystemVersionQueryHandler(IApplicationDbContext applicationDbContext) : IRequestHandler<CheckSystemVersionRequest, bool>
{
    public async Task<bool> Handle(CheckSystemVersionRequest request, CancellationToken cancellationToken)
    {
       var systemVersion = await applicationDbContext.SystemVersions.FirstOrDefaultAsync(x=>x.Platform == request.Platform,cancellationToken)
                            ?? throw new NotFoundException($"Can not find platform {request.Platform}");
       return systemVersion.Version == request.Version;
    }
}