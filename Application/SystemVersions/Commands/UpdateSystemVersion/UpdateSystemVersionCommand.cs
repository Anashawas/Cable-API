using Application.SystemVersions.Commands.AddSystemVersion;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.SystemVersions.Commands.AddSystemVersionUpdate;

public record UpdateSystemVersionCommand(int Id, string Platform, string Version, bool UpdateForce) : IRequest;

public class UpdateSystemVersionCommandHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<UpdateSystemVersionCommand>
{
    public async Task Handle(UpdateSystemVersionCommand request, CancellationToken cancellationToken)
    {
        var systemVersion =
            await applicationDbContext.SystemVersions.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken) ??
            throw new NotFoundException($"System version not found with id: {request.Id} ");
        systemVersion.Platform = request.Platform;
        systemVersion.Version = request.Version;
        systemVersion.ForceUpdate =  request.UpdateForce;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}