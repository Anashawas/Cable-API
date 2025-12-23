using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.SharedLinks.Queries;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Persistence.Repositories;

public class SharedLinkRepository(ApplicationDbContext applicationDbContext) : ISharedLinkRepository
{
    public async Task<List<SharedLinkDto>> GetSharedLinksByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        var links = await applicationDbContext.SharedLinks
            .AsNoTracking()
            .Where(x => x.CreatedBy == userId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new SharedLinkDto(
                x.Id,
                x.LinkToken,
                x.LinkType,
                x.TargetId,
                x.Parameters,
                x.ExpiresAt,
                x.MaxUsage,
                x.CurrentUsage,
                x.IsActive,
                null
            ))
            .ToListAsync(cancellationToken);

        // Get base URLs for each link type
        var linkTypes = await applicationDbContext.SharedLinkTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.TypeName, x => x.BaseUrl, cancellationToken);

        // Update DTOs with base URLs
        var result = links.Select(link => link with { BaseUrl = linkTypes.GetValueOrDefault(link.LinkType) }).ToList();

        return result;
    }

    public async Task<List<SharedLinkUsageDto>> GetSharedLinkUsageAsync(int sharedLinkId, CancellationToken cancellationToken)
    {
        return await applicationDbContext.SharedLinkUsages
            .AsNoTracking()
            .Where(x => x.SharedLinkId == sharedLinkId)
            .OrderByDescending(x => x.UsedAt)
            .Select(x => new SharedLinkUsageDto(
                x.Id,
                x.SharedLinkId,
                x.UserId,
                x.DeviceInfo,
                x.IpAddress,
                x.UsedAt,
                x.IsSuccessful,
                x.ErrorMessage
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsTokenUniqueAsync(string token, CancellationToken cancellationToken)
    {
        return !await applicationDbContext.SharedLinks
            .AnyAsync(x => x.LinkToken == token && !x.IsDeleted, cancellationToken);
    }

    public async Task<List<SharedLinkTypeDto>> GetAllSharedLinkTypesAsync(CancellationToken cancellationToken)
    {
        return await applicationDbContext.SharedLinkTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new SharedLinkTypeDto(
                x.Id,
                x.TypeName,
                x.Description,
                x.BaseUrl,
                x.IsActive
            ))
            .ToListAsync(cancellationToken);
    }

    public async Task CleanupExpiredLinksAsync(CancellationToken cancellationToken)
    {
        var expiredLinks = await applicationDbContext.SharedLinks
            .Where(x => x.ExpiresAt.HasValue && x.ExpiresAt.Value <= DateTime.Now && !x.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var link in expiredLinks)
        {
            link.IsActive = false;
        }

        if (expiredLinks.Any())
        {
            await applicationDbContext.SaveChanges(cancellationToken);
        }
    }
}