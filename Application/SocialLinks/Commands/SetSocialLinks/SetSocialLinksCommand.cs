using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.SocialLinks.Commands.SetSocialLinks;

public record SocialLinkInput(
    int    SocialMediaPlatformId,
    string Url,
    int    DisplayOrder = 0
);

/// <summary>
/// Admin-only. Replaces the full set of social links for a given (ProviderType, ProviderId).
/// Existing rows are soft-deleted and the new list is inserted in one transaction.
/// </summary>
public record SetSocialLinksCommand(
    string                ProviderType,
    int                   ProviderId,
    List<SocialLinkInput> Links
) : IRequest<int[]>;

public class SetSocialLinksCommandHandler(
    IApplicationDbContext applicationDbContext)
    : IRequestHandler<SetSocialLinksCommand, int[]>
{
    public async Task<int[]> Handle(SetSocialLinksCommand request, CancellationToken cancellationToken)
    {
        // Validate that the parent entity exists.
        var parentExists = request.ProviderType switch
        {
            "ServiceProvider" => await applicationDbContext.ServiceProviders
                                     .AnyAsync(x => x.Id == request.ProviderId && !x.IsDeleted, cancellationToken),
            "ChargingPoint"   => await applicationDbContext.ChargingPoints
                                     .AnyAsync(x => x.Id == request.ProviderId, cancellationToken),
            _                 => false
        };

        if (!parentExists)
            throw new NotFoundException($"{request.ProviderType} with id {request.ProviderId} not found.");

        // Validate every platform reference exists and is active.
        var platformIds = request.Links.Select(l => l.SocialMediaPlatformId).Distinct().ToList();
        var validPlatformIds = await applicationDbContext.SocialMediaPlatforms
            .Where(p => platformIds.Contains(p.Id) && !p.IsDeleted && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var missing = platformIds.Except(validPlatformIds).ToList();
        if (missing.Count > 0)
            throw new NotFoundException(
                $"SocialMediaPlatform id(s) not found or inactive: {string.Join(", ", missing)}");

        // Soft-delete any existing links for this provider.
        var existing = await applicationDbContext.SocialLinks
            .Where(l => l.ProviderType == request.ProviderType
                     && l.ProviderId   == request.ProviderId
                     && !l.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var row in existing)
            row.IsDeleted = true;

        // Insert the new set.
        var toAdd = request.Links.Select(input => new SocialLink
        {
            ProviderType          = request.ProviderType,
            ProviderId            = request.ProviderId,
            SocialMediaPlatformId = input.SocialMediaPlatformId,
            Url                   = input.Url,
            DisplayOrder          = input.DisplayOrder
        }).ToList();

        applicationDbContext.SocialLinks.AddRange(toAdd);
        await applicationDbContext.SaveChanges(cancellationToken);

        return toAdd.Select(x => x.Id).ToArray();
    }
}
