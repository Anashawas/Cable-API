using System.Globalization;
using Application.Common.Security;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.Core.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Application.Billing;

/// <summary>
/// Billing is the owner's business (decision 6): the station / provider owner, or an admin for
/// support. Workers — even with every privilege — do not see plans, prices or payments, which is
/// why this does not reuse ProviderAccessGuard.
/// </summary>
public static class BillingAccessGuard
{
    public static async Task EnsureOwnerOrAdminAsync(IApplicationDbContext db, ICurrentUserService currentUser,
        string providerType, int providerId, CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new NotAuthorizedAccessException("User not authenticated");
        if (!BillingProviderTypes.IsValid(providerType))
            throw new DataValidationException("ProviderType", Lang.Pick("نوع المزوّد يجب أن يكون ChargingPoint أو ServiceProvider", "ProviderType must be ChargingPoint or ServiceProvider"));

        var owner = await OwnerOfAsync(db, providerType, providerId, ct);
        if (owner.Exists == false)
            throw new NotFoundException(Lang.Pick($"لم يتم العثور على {providerType} رقم {providerId}", $"{providerType} with id {providerId} not found"));

        if (owner.OwnerId == userId) return;
        if (await AdminRoleGuard.IsAdminAsync(db, currentUser, ct)) return;

        throw new ForbiddenAccessException(Lang.Pick("الفوترة متاحة لمالك المحطة فقط", "Billing is available to the owner only"));
    }

    public static async Task<(bool Exists, int? OwnerId, string? Name)> OwnerOfAsync(IApplicationDbContext db, string providerType, int providerId, CancellationToken ct)
    {
        if (providerType == BillingProviderTypes.ChargingPoint)
        {
            var cp = await db.ChargingPoints.AsNoTracking().Where(x => x.Id == providerId && !x.IsDeleted)
                .Select(x => new { x.OwnerId, x.Name }).FirstOrDefaultAsync(ct);
            return (cp != null, cp?.OwnerId, cp?.Name);
        }
        var sp = await db.ServiceProviders.AsNoTracking().Where(x => x.Id == providerId && !x.IsDeleted)
            .Select(x => new { x.OwnerId, x.Name }).FirstOrDefaultAsync(ct);
        return (sp != null, sp?.OwnerId, sp?.Name);
    }
}

/// <summary>Typed readers for the Billing.* AppSetting rows, with the document's defaults.</summary>
public static class BillingSettingsProvider
{
    public static async Task<double> GetPointsPerJodAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var raw = await Read(db, BillingSettings.PointsPerJodKey, ct);
        if (raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0) return v;

        // Fall back to the default points conversion rate the percentage model already uses.
        var rate = await db.PointsConversionRates.AsNoTracking()
            .Where(x => x.IsDefault && x.IsActive && !x.IsDeleted)
            .Select(x => (double?)x.PointsPerUnit)
            .FirstOrDefaultAsync(ct);
        return rate is > 0 ? rate.Value : BillingSettings.DefaultPointsPerJod;
    }

    public static async Task<decimal> GetTargetMarginPercentAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var raw = await Read(db, BillingSettings.TargetMarginPercentKey, ct);
        return raw is not null && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 100
            ? v : BillingSettings.DefaultTargetMarginPercent;
    }

    public static async Task<decimal> GetBundleMinimumAmountAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var raw = await Read(db, BillingSettings.BundleMinimumAmountKey, ct);
        return raw is not null && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v : BillingSettings.DefaultBundleMinimumAmount;
    }

    private static Task<string?> Read(IApplicationDbContext db, string key, CancellationToken ct) =>
        db.AppSettings.AsNoTracking().Where(s => s.Key == key && !s.IsDeleted).Select(s => s.Value).FirstOrDefaultAsync(ct);
}
