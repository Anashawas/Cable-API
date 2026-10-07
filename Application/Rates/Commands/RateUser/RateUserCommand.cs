using Application.Common.Security;
using Cable.Core;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Application.Rates.Commands.RateUser;

/// <summary>
/// A provider rates the driver it just served. Anchored to a completed
/// <c>PartnerTransaction</c> — the provider cannot rate a driver it has no
/// record of serving, and cannot rate the same visit twice.
/// </summary>
public record RateUserCommand(int PartnerTransactionId, int Rating, string? Comment = null) : IRequest<int>;

public class RateUserCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<RateUserCommand, int>
{
    public async Task<int> Handle(RateUserCommand request, CancellationToken cancellationToken)
    {
        var raterId = currentUserService.UserId
                      ?? throw new NotAuthorizedAccessException("User not authenticated");

        var transaction = await applicationDbContext.PartnerTransactions
                              .AsNoTracking()
                              .FirstOrDefaultAsync(x => x.Id == request.PartnerTransactionId && !x.IsDeleted,
                                  cancellationToken)
                          ?? throw new NotFoundException($"Partner transaction with id {request.PartnerTransactionId} not found");

        // Only a finished visit can be rated. An Initiated code has not been
        // scanned yet, so nobody has been served; Expired and Cancelled never were.
        if (transaction.Status != (int)PartnerTransactionStatus.Completed)
            throw new DataValidationException("PartnerTransactionId",
                "Only a completed transaction can be rated");

        // Completed always carries the scanning driver, but the column is nullable
        // for the Initiated stage — so this is a real branch, not a formality.
        var driverId = transaction.UserId
                       ?? throw new DataValidationException("PartnerTransactionId",
                           "This transaction has no driver to rate");

        await EnsureCallerRepresentsProvider(transaction.ProviderType, transaction.ProviderId, raterId,
            cancellationToken);

        // A provider owner who is also the scanning driver would otherwise be
        // able to rate themselves.
        if (driverId == raterId)
            throw new ForbiddenAccessException("You cannot rate yourself.");

        // The unique index is the real guarantee; this check exists to return a
        // clear message instead of a constraint violation.
        var alreadyRated = await applicationDbContext.UserRates
            .AnyAsync(x => x.PartnerTransactionId == transaction.Id && !x.IsDeleted, cancellationToken);

        if (alreadyRated)
            throw new DataValidationException("PartnerTransactionId",
                "This transaction has already been rated");

        var userRate = new UserRate
        {
            UserId = driverId,
            RatedByUserId = raterId,
            ProviderType = transaction.ProviderType,
            ProviderId = transaction.ProviderId,
            PartnerTransactionId = transaction.Id,
            Rating = request.Rating,
            Comment = request.Comment
        };

        applicationDbContext.UserRates.Add(userRate);
        await applicationDbContext.SaveChanges(cancellationToken);

        return userRate.Id;
    }

    /// <summary>
    /// Owner, active worker, or admin — the same access rule the provider-facing
    /// commands use (see SendFavoritesNotificationCommand).
    /// </summary>
    private async Task EnsureCallerRepresentsProvider(string providerType, int providerId, int raterId,
        CancellationToken cancellationToken)
    {
        var ownerId = providerType switch
        {
            "ChargingPoint" => await applicationDbContext.ChargingPoints.AsNoTracking()
                .Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken),
            "ServiceProvider" => await applicationDbContext.ServiceProviders.AsNoTracking()
                .Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => x.OwnerId)
                .FirstOrDefaultAsync(cancellationToken),
            _ => throw new DataValidationException("ProviderType",
                $"Unsupported provider type '{providerType}'")
        };

        if (ownerId == raterId)
            return;

        var isWorker = await applicationDbContext.ProviderManagers.AsNoTracking()
            .AnyAsync(pm => pm.ProviderType == providerType
                            && pm.ProviderId == providerId
                            && pm.UserId == raterId
                            && pm.IsActive && !pm.IsDeleted, cancellationToken);

        if (isWorker)
            return;

        if (await AdminRoleGuard.IsAdminAsync(applicationDbContext, currentUserService, cancellationToken))
            return;

        throw new ForbiddenAccessException(
            "Only the provider owner, its worker, or an admin can rate this transaction's driver.");
    }
}
