using Application.SharedLinks.Queries;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.SharedLinks.Queries.GetSharedLinkByToken;

public record GetSharedLinkByTokenRequest(string LinkToken) : IRequest<SharedLinkDto>;

public class GetSharedLinkByTokenRequestHandler(IApplicationDbContext applicationDbContext)
    : IRequestHandler<GetSharedLinkByTokenRequest, SharedLinkDto>
{
    public async Task<SharedLinkDto> Handle(GetSharedLinkByTokenRequest request, CancellationToken cancellationToken)
    {
        var sharedLink = await applicationDbContext.SharedLinks
                             .AsNoTracking()
                             .Include(x => x.SharedLinkUsages)
                             .FirstOrDefaultAsync(x => x.LinkToken == request.LinkToken && !x.IsDeleted, cancellationToken)
                         ?? throw new NotFoundException($"Shared link with token '{request.LinkToken}' not found");

        var linkType = await applicationDbContext.SharedLinkTypes
                           .AsNoTracking()
                           .FirstOrDefaultAsync(x => x.TypeName == sharedLink.LinkType && x.IsActive, cancellationToken);

        return new SharedLinkDto(
            sharedLink.Id,
            sharedLink.LinkToken,
            sharedLink.LinkType,
            sharedLink.TargetId,
            sharedLink.Parameters,
            sharedLink.ExpiresAt,
            sharedLink.MaxUsage,
            sharedLink.CurrentUsage,
            sharedLink.IsActive,
            linkType?.BaseUrl
        );
    }
}