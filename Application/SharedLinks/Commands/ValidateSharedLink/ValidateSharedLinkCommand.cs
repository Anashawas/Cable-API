using Application.SharedLinks.Queries;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.SharedLinks.Commands.ValidateSharedLink;

public record ValidateSharedLinkCommand(
    string LinkToken,
    string? DeviceInfo,
    string? IpAddress
) : IRequest<ValidateSharedLinkResult>;

public record ValidateSharedLinkResult(
    bool IsValid,
    string? ErrorMessage,
    SharedLinkDto? SharedLink
);

public class ValidateSharedLinkCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<ValidateSharedLinkCommand, ValidateSharedLinkResult>
{
    public async Task<ValidateSharedLinkResult> Handle(ValidateSharedLinkCommand request, CancellationToken cancellationToken)
    {
        var sharedLink = await applicationDbContext.SharedLinks
            .FirstOrDefaultAsync(x => x.LinkToken == request.LinkToken && !x.IsDeleted, cancellationToken);

        if (sharedLink == null)
        {
            return new ValidateSharedLinkResult(false, "Link not found", null);
        }

        if (!sharedLink.IsActive)
        {
            return new ValidateSharedLinkResult(false, "Link is inactive", null);
        }

        if (sharedLink.ExpiresAt.HasValue && sharedLink.ExpiresAt.Value <= DateTime.Now)
        {
            return new ValidateSharedLinkResult(false, "Link has expired", null);
        }

        if (sharedLink.CurrentUsage >= sharedLink.MaxUsage)
        {
            return new ValidateSharedLinkResult(false, "Link usage limit exceeded", null);
        }

        var linkType = await applicationDbContext.SharedLinkTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TypeName == sharedLink.LinkType && x.IsActive, cancellationToken);

        try
        {
            sharedLink.CurrentUsage++;
            
            var usage = new SharedLinkUsage
            {
                SharedLinkId = sharedLink.Id,
                UserId = currentUserService.UserId,
                DeviceInfo = request.DeviceInfo,
                IpAddress = request.IpAddress,
                UsedAt = DateTime.Now,
                IsSuccessful = true
            };

            applicationDbContext.SharedLinkUsages.Add(usage);
            await applicationDbContext.SaveChanges(cancellationToken);

            var sharedLinkDto = new SharedLinkDto(
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

            return new ValidateSharedLinkResult(true, null, sharedLinkDto);
        }
        catch (Exception ex)
        {
            var errorUsage = new SharedLinkUsage
            {
                SharedLinkId = sharedLink.Id,
                UserId = currentUserService.UserId,
                DeviceInfo = request.DeviceInfo,
                IpAddress = request.IpAddress,
                UsedAt = DateTime.Now,
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };

            applicationDbContext.SharedLinkUsages.Add(errorUsage);
            await applicationDbContext.SaveChanges(cancellationToken);

            return new ValidateSharedLinkResult(false, $"Error processing link: {ex.Message}", null);
        }
    }
}