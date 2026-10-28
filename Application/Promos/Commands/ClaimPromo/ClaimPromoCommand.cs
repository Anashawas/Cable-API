using Application.Common.Extensions;
using Application.Common.Utilities;
using Cable.Core;
using Cable.Core.Enums;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Application.Promos.Commands.ClaimPromo;

/// <summary>
/// POST /api/promos/claim { "code": "CABLE20" } — adds the promo to "My codes". Case-insensitive; spaces and dashes
/// are ignored. Errors (422, field "code"): PROMO_NOT_FOUND, PROMO_EXPIRED, PROMO_ALREADY_CLAIMED, PROMO_LIMIT_REACHED,
/// PROMO_NOT_ELIGIBLE, PROMO_NOT_STARTED, PROMO_ALREADY_USED, TOO_MANY_ATTEMPTS (10 wrong codes per hour).
/// </summary>
public record ClaimPromoCommand(string Code) : IRequest<MyPromoDto>;

public class ClaimPromoCommandValidator : AbstractValidator<ClaimPromoCommand>
{
    public ClaimPromoCommandValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(60);
}

public class ClaimPromoCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ClaimPromoCommand, MyPromoDto>
{
    public async Task<MyPromoDto> Handle(ClaimPromoCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        var now = DateTime.UtcNow;

        // Guessing protection: 10 wrong codes per user per rolling hour.
        var hourAgo = now.AddHours(-1);
        var wrong = await db.PromoClaimAttempts.CountAsync(a => a.UserId == userId && a.AttemptedAt > hourAgo, cancellationToken);
        if (wrong >= PromoRules.MaxWrongAttemptsPerHour)
            throw PromoErrorCodes.Error(PromoErrorCodes.TooManyAttempts,
                new Dictionary<string, object?> { ["retryAfterMinutes"] = 60 });

        var normalized = SafeCode.Normalize(request.Code);

        // Public code first (CABLE20), then a unique code (PRM-…).
        var campaignId = await db.PromoCampaigns.AsNoTracking()
            .Where(c => c.CodeMode == PromoCodeMode.Public && c.PublicCodeNormalized == normalized && !c.IsDeleted)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        PromoCode? uniqueCode = null;
        if (campaignId is null && normalized.Length > 0)
        {
            uniqueCode = await db.PromoCodes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.NormalizedCode == normalized, cancellationToken);
            campaignId = uniqueCode?.PromoCampaignId;
        }

        if (campaignId is null)
        {
            db.PromoClaimAttempts.Add(new PromoClaimAttempt
            {
                UserId = userId,
                CodeTried = normalized.Length > 60 ? normalized[..60] : normalized,
                AttemptedAt = now
            });
            await db.SaveChanges(cancellationToken);
            throw PromoErrorCodes.Error(PromoErrorCodes.NotFound);
        }

        var accountCreatedAt = await db.UserAccounts.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialises claims of one campaign, so totalLimit and the per-user checks hold under load.
            var campaign = await db.PromoCampaigns.FindWithLockAsync("PromoCampaign", campaignId.Value, cancellationToken)
                           ?? throw PromoErrorCodes.Error(PromoErrorCodes.NotFound);

            if (PromoRules.WhyCannotClaim(campaign, PromoRules.IsNewUser(accountCreatedAt, now), now) is { } reason)
                throw PromoErrorCodes.Error(reason);

            if (uniqueCode is not null)
            {
                var code = await db.PromoCodes
                               .FromSqlRaw("SELECT * FROM [PromoCode] WITH (UPDLOCK, ROWLOCK) WHERE Id = {0}", uniqueCode.Id)
                               .FirstOrDefaultAsync(cancellationToken)
                           ?? throw PromoErrorCodes.Error(PromoErrorCodes.NotFound);
                if (code.ClaimedByUserId == userId) throw PromoErrorCodes.Error(PromoErrorCodes.AlreadyClaimed);
                if (code.ClaimedByUserId is not null) throw PromoErrorCodes.Error(PromoErrorCodes.AlreadyUsed);
                code.ClaimedByUserId = userId;
                code.ClaimedAt = now;
            }

            var mine = await db.UserPromos
                .Where(u => u.UserId == userId && u.PromoCampaignId == campaign.Id && !u.IsDeleted)
                .ToListAsync(cancellationToken);

            var available = mine.FirstOrDefault(u => u.Status == UserPromoStatus.Available);
            if (available is not null)
            {
                if (PromoRules.EffectiveStatus(available.Status, available.ExpiresAt, now) == UserPromoStatus.Available)
                    throw PromoErrorCodes.Error(PromoErrorCodes.AlreadyClaimed);
                available.Status = UserPromoStatus.Expired;   // past its date; frees the "one available" slot
            }

            // Every promo the user had of this campaign counts (used, expired unused, revoked) — otherwise
            // validDaysAfterClaim could be renewed forever by adding the public code again.
            if (mine.Count >= campaign.PerUserLimit)
                throw PromoErrorCodes.Error(
                    mine.Any(u => u.Status == UserPromoStatus.Used) ? PromoErrorCodes.AlreadyUsed
                    : mine.Any(u => u.Status == UserPromoStatus.Revoked) ? PromoErrorCodes.NotEligible
                    : PromoErrorCodes.Expired);

            var userPromo = new UserPromo
            {
                UserId = userId,
                PromoCampaignId = campaign.Id,
                PromoCodeId = uniqueCode?.Id,
                Source = UserPromoSource.Code,
                ClaimedAt = now,
                ExpiresAt = PromoRules.ExpiryFor(campaign.EndsAt, campaign.ValidDaysAfterClaim, now),
                Status = UserPromoStatus.Available
            };
            db.UserPromos.Add(userPromo);
            campaign.ClaimsCount += 1;

            await db.SaveChanges(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return PromoViews.ToDto(userPromo, campaign, now);
        }
        catch (DbUpdateException)
        {
            // The "one available promo per user and campaign" index caught a double tap.
            await transaction.RollbackAsync(cancellationToken);
            throw PromoErrorCodes.Error(PromoErrorCodes.AlreadyClaimed);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
