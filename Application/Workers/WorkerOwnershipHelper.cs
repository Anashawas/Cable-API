using Application.Common.Interfaces;
using Cable.Core;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Workers;

/// <summary>
/// Shared guard: only the owner of a provider may manage its worker.
/// </summary>
internal static class WorkerOwnershipHelper
{
    public const string ServiceProvider = "ServiceProvider";
    public const string ChargingPoint   = "ChargingPoint";

    public static async Task EnsureCallerOwnsProviderAsync(
        IApplicationDbContext db,
        string providerType,
        int providerId,
        int callerUserId,
        CancellationToken cancellationToken)
    {
        var provider = providerType switch
        {
            ServiceProvider => await db.ServiceProviders
                .Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => new { x.OwnerId })
                .FirstOrDefaultAsync(cancellationToken),
            ChargingPoint => await db.ChargingPoints
                .Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => new { x.OwnerId })
                .FirstOrDefaultAsync(cancellationToken),
            _ => null
        };

        if (provider is null)
            throw new NotFoundException($"{providerType} with id {providerId} not found");

        if (provider.OwnerId is null)
            throw new ForbiddenAccessException("This provider has no owner assigned.");

        if (provider.OwnerId.Value != callerUserId)
            throw new ForbiddenAccessException("Only the provider owner can manage its worker.");
    }
}
