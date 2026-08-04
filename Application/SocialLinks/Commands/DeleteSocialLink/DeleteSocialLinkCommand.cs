using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.SocialLinks.Commands.DeleteSocialLink;

public record DeleteSocialLinkCommand(int Id) : IRequest;

public class DeleteSocialLinkCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<DeleteSocialLinkCommand>
{
    public async Task Handle(DeleteSocialLinkCommand request, CancellationToken cancellationToken)
    {
        var link = await applicationDbContext.SocialLinks
                       .FirstOrDefaultAsync(l => l.Id == request.Id && !l.IsDeleted, cancellationToken)
                   ?? throw new NotFoundException(nameof(SocialLink), request.Id);

        link.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
