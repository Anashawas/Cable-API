using Application.SharedLinks.Queries;

namespace Application.Common.Interfaces.Repositories;

public interface ISharedLinkRepository
{
    Task<List<SharedLinkDto>> GetSharedLinksByUserIdAsync(int userId, CancellationToken cancellationToken);
    Task<List<SharedLinkUsageDto>> GetSharedLinkUsageAsync(int sharedLinkId, CancellationToken cancellationToken);
    Task<bool> IsTokenUniqueAsync(string token, CancellationToken cancellationToken);
    Task<List<SharedLinkTypeDto>> GetAllSharedLinkTypesAsync(CancellationToken cancellationToken);
    Task CleanupExpiredLinksAsync(CancellationToken cancellationToken);
}