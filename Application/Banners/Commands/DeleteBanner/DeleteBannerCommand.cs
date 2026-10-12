using Application.Common.Security;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Banners.Commands.DeleteBanner;

public record DeleteBannerCommand(int Id) : IRequest;

public class DeleteBannerCommandHandler(IApplicationDbContext applicationDbContext, ICurrentUserService currentUserService)
    : IRequestHandler<DeleteBannerCommand>
{
    public async Task Handle(DeleteBannerCommand request, CancellationToken cancellationToken)
    {
        // Banners appear on every user's home screen: only the Cable team may change them.
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);
        var banner =
            await applicationDbContext.Banners.FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted,
                cancellationToken)
            ?? throw new NotFoundException($"Can't find banner with id {request.Id}");

        banner.IsDeleted = true;
        await applicationDbContext.SaveChanges(cancellationToken);
    }
}