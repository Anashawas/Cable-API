using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.SocialMediaPlatforms.Commands.DeleteSocialMediaPlatform;

public record DeleteSocialMediaPlatformCommand(int Id) : IRequest;

public class DeleteSocialMediaPlatformCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteSocialMediaPlatformCommand>
{
    public async Task Handle(DeleteSocialMediaPlatformCommand request, CancellationToken cancellationToken)
    {
        var platform = await applicationDbContext.SocialMediaPlatforms
                           .FirstOrDefaultAsync(p => p.Id == request.Id && !p.IsDeleted, cancellationToken)
                       ?? throw new NotFoundException(nameof(SocialMediaPlatform), request.Id);

        // Soft delete — existing SocialLink rows that point at this platform stay valid,
        // but the platform stops appearing in the active catalog.
        platform.IsDeleted = true;
        platform.IsActive  = false;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
