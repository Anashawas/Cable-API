using Application.Common.Interfaces;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Utilities;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Loyalty.Commands.ManageBoosts;

public record BoostProviderDto(string ProviderType, int ProviderId);

public record LoyaltyBoostDto(
    int Id,
    string Name,
    string? NameAr,
    string? Description,
    double Multiplier,
    DateTime StartsAt,
    DateTime EndsAt,
    int? DailyStartMinute,
    int? DailyEndMinute,
    int? DaysOfWeekMask,
    bool AppliesToAllProviders,
    int Priority,
    int? MaxBonusPointsPerUser,
    int? MaxTotalBonusPoints,
    bool IsActive,
    DateTime CreatedAt,
    List<BoostProviderDto> Providers,
    /// <summary>Bonus points handed out so far — the campaign's cost to date.</summary>
    int BonusPointsSpent,
    int BoostedTransactions);

public record CreateLoyaltyBoostCommand(
    string Name,
    string? NameAr,
    string? Description,
    double Multiplier,
    DateTime StartsAt,
    DateTime EndsAt,
    int? DailyStartMinute,
    int? DailyEndMinute,
    int? DaysOfWeekMask,
    bool AppliesToAllProviders,
    int Priority,
    int? MaxBonusPointsPerUser,
    int? MaxTotalBonusPoints,
    List<BoostProviderDto>? Providers
) : IRequest<int>;

public class CreateLoyaltyBoostCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateLoyaltyBoostCommand, int>
{
    public async Task<int> Handle(CreateLoyaltyBoostCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var userId = currentUserService.UserId;

        var boost = new LoyaltyBoost
        {
            Name = request.Name.Trim(),
            NameAr = request.NameAr?.Trim(),
            Description = request.Description?.Trim(),
            Multiplier = request.Multiplier,
            // Entered as Amman wall-clock; the resolver compares against UTC.
            StartsAt = JordanTime.ToUtc(request.StartsAt),
            EndsAt = JordanTime.ToUtc(request.EndsAt),
            DailyStartMinute = request.DailyStartMinute,
            DailyEndMinute = request.DailyEndMinute,
            DaysOfWeekMask = request.DaysOfWeekMask,
            AppliesToAllProviders = request.AppliesToAllProviders,
            Priority = request.Priority,
            MaxBonusPointsPerUser = request.MaxBonusPointsPerUser,
            MaxTotalBonusPoints = request.MaxTotalBonusPoints,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };

        if (!request.AppliesToAllProviders)
        {
            foreach (var provider in (request.Providers ?? []).DistinctBy(p => (p.ProviderType, p.ProviderId)))
            {
                boost.Providers.Add(new LoyaltyBoostProvider
                {
                    ProviderType = provider.ProviderType,
                    ProviderId = provider.ProviderId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId
                });
            }
        }

        applicationDbContext.LoyaltyBoosts.Add(boost);
        await applicationDbContext.SaveChanges(cancellationToken);

        return boost.Id;
    }
}

public record UpdateLoyaltyBoostCommand(
    int Id,
    string Name,
    string? NameAr,
    string? Description,
    double Multiplier,
    DateTime StartsAt,
    DateTime EndsAt,
    int? DailyStartMinute,
    int? DailyEndMinute,
    int? DaysOfWeekMask,
    bool AppliesToAllProviders,
    int Priority,
    int? MaxBonusPointsPerUser,
    int? MaxTotalBonusPoints,
    List<BoostProviderDto>? Providers
) : IRequest;

public class UpdateLoyaltyBoostCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateLoyaltyBoostCommand>
{
    public async Task Handle(UpdateLoyaltyBoostCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var boost = await applicationDbContext.LoyaltyBoosts
                        .Include(b => b.Providers)
                        .FirstOrDefaultAsync(b => b.Id == request.Id && !b.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Loyalty boost {request.Id} not found");

        // A campaign that has started is a record of terms customers were given.
        // BaseAuditableEntity timestamps an edit but does not version it, so a
        // mid-flight rate change would leave nothing to show what the original
        // terms were. Ending one early is legitimate — see DeactivateLoyaltyBoost.
        if (boost.StartsAt <= DateTime.UtcNow)
            throw new DataValidationException("StartsAt",
                "This boost has already started and can no longer be edited. Deactivate it instead.");

        var userId = currentUserService.UserId;

        boost.Name = request.Name.Trim();
        boost.NameAr = request.NameAr?.Trim();
        boost.Description = request.Description?.Trim();
        boost.Multiplier = request.Multiplier;
        boost.StartsAt = JordanTime.ToUtc(request.StartsAt);
        boost.EndsAt = JordanTime.ToUtc(request.EndsAt);
        boost.DailyStartMinute = request.DailyStartMinute;
        boost.DailyEndMinute = request.DailyEndMinute;
        boost.DaysOfWeekMask = request.DaysOfWeekMask;
        boost.AppliesToAllProviders = request.AppliesToAllProviders;
        boost.Priority = request.Priority;
        boost.MaxBonusPointsPerUser = request.MaxBonusPointsPerUser;
        boost.MaxTotalBonusPoints = request.MaxTotalBonusPoints;
        boost.ModifiedAt = DateTime.UtcNow;
        boost.ModifiedBy = userId;

        applicationDbContext.LoyaltyBoostProviders.RemoveRange(boost.Providers);

        if (!request.AppliesToAllProviders)
        {
            foreach (var provider in (request.Providers ?? []).DistinctBy(p => (p.ProviderType, p.ProviderId)))
            {
                boost.Providers.Add(new LoyaltyBoostProvider
                {
                    LoyaltyBoostId = boost.Id,
                    ProviderType = provider.ProviderType,
                    ProviderId = provider.ProviderId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId
                });
            }
        }

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}

/// <summary>Ends a boost immediately. Allowed at any point, including mid-campaign.</summary>
public record DeactivateLoyaltyBoostCommand(int Id) : IRequest;

public class DeactivateLoyaltyBoostCommandHandler(
    IApplicationDbContext applicationDbContext,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeactivateLoyaltyBoostCommand>
{
    public async Task Handle(DeactivateLoyaltyBoostCommand request, CancellationToken cancellationToken)
    {
        await AdminRoleGuard.EnsureAdminAsync(applicationDbContext, currentUserService, cancellationToken);

        var boost = await applicationDbContext.LoyaltyBoosts
                        .FirstOrDefaultAsync(b => b.Id == request.Id && !b.IsDeleted, cancellationToken)
                    ?? throw new NotFoundException($"Loyalty boost {request.Id} not found");

        boost.IsActive = false;
        boost.ModifiedAt = DateTime.UtcNow;
        boost.ModifiedBy = currentUserService.UserId;

        await applicationDbContext.SaveChanges(cancellationToken);
    }
}
