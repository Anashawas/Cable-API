using FluentValidation;

namespace Application.Loyalty.Commands.ManageBoosts;

/// <summary>
/// Shared rules, so create and update can never drift apart and let an invalid
/// campaign in through the edit path.
/// </summary>
internal static class LoyaltyBoostRules
{
    private const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// Upper bound is a typo guard, not a business limit: this multiplies points
    /// that cost real money, and a slipped decimal turning 2 into 20 should not
    /// reach production.
    /// </summary>
    private const double MaxMultiplier = 10.0;

    public static void Apply<T>(AbstractValidator<T> validator,
        Func<T, double> multiplier,
        Func<T, string> name,
        Func<T, DateTime> startsAt,
        Func<T, DateTime> endsAt,
        Func<T, int?> dailyStart,
        Func<T, int?> dailyEnd,
        Func<T, int?> daysMask,
        Func<T, bool> appliesToAll,
        Func<T, int> providerCount,
        Func<T, int?> maxPerUser,
        Func<T, int?> maxTotal)
    {
        validator.RuleFor(x => name(x))
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(150);

        validator.RuleFor(x => multiplier(x))
            .GreaterThan(1.0).WithMessage("Multiplier must be greater than 1 — a boost that does not increase points is not a boost.")
            .LessThanOrEqualTo(MaxMultiplier).WithMessage($"Multiplier must not exceed {MaxMultiplier}.");

        validator.RuleFor(x => x)
            .Must(x => endsAt(x) > startsAt(x))
            .WithMessage("EndsAt must be after StartsAt.");

        // Both bounds or neither: one alone has no meaning.
        validator.RuleFor(x => x)
            .Must(x => dailyStart(x).HasValue == dailyEnd(x).HasValue)
            .WithMessage("DailyStartMinute and DailyEndMinute must both be set, or both be omitted.");

        validator.RuleFor(x => dailyStart(x))
            .InclusiveBetween(0, MinutesPerDay - 1)
            .When(x => dailyStart(x).HasValue)
            .WithMessage($"DailyStartMinute must be between 0 and {MinutesPerDay - 1}.");

        validator.RuleFor(x => dailyEnd(x))
            .InclusiveBetween(0, MinutesPerDay - 1)
            .When(x => dailyEnd(x).HasValue)
            .WithMessage($"DailyEndMinute must be between 0 and {MinutesPerDay - 1}.");

        // Equal bounds would be an empty window, which silently never fires.
        validator.RuleFor(x => x)
            .Must(x => dailyStart(x) != dailyEnd(x))
            .When(x => dailyStart(x).HasValue && dailyEnd(x).HasValue)
            .WithMessage("DailyStartMinute and DailyEndMinute must differ; equal values describe an empty window.");

        validator.RuleFor(x => daysMask(x))
            .InclusiveBetween(1, 127)
            .When(x => daysMask(x).HasValue)
            .WithMessage("DaysOfWeekMask must be between 1 and 127; 0 would exclude every day.");

        validator.RuleFor(x => x)
            .Must(x => appliesToAll(x) || providerCount(x) > 0)
            .WithMessage("List at least one provider, or set AppliesToAllProviders.");

        validator.RuleFor(x => maxPerUser(x))
            .GreaterThan(0).When(x => maxPerUser(x).HasValue)
            .WithMessage("MaxBonusPointsPerUser must be greater than 0.");

        validator.RuleFor(x => maxTotal(x))
            .GreaterThan(0).When(x => maxTotal(x).HasValue)
            .WithMessage("MaxTotalBonusPoints must be greater than 0.");
    }
}

public class CreateLoyaltyBoostCommandValidator : AbstractValidator<CreateLoyaltyBoostCommand>
{
    public CreateLoyaltyBoostCommandValidator()
        => LoyaltyBoostRules.Apply(this,
            x => x.Multiplier, x => x.Name, x => x.StartsAt, x => x.EndsAt,
            x => x.DailyStartMinute, x => x.DailyEndMinute, x => x.DaysOfWeekMask,
            x => x.AppliesToAllProviders, x => x.Providers?.Count ?? 0,
            x => x.MaxBonusPointsPerUser, x => x.MaxTotalBonusPoints);
}

public class UpdateLoyaltyBoostCommandValidator : AbstractValidator<UpdateLoyaltyBoostCommand>
{
    public UpdateLoyaltyBoostCommandValidator()
        => LoyaltyBoostRules.Apply(this,
            x => x.Multiplier, x => x.Name, x => x.StartsAt, x => x.EndsAt,
            x => x.DailyStartMinute, x => x.DailyEndMinute, x => x.DaysOfWeekMask,
            x => x.AppliesToAllProviders, x => x.Providers?.Count ?? 0,
            x => x.MaxBonusPointsPerUser, x => x.MaxTotalBonusPoints);
}
