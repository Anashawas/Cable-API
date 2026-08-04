using Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Application.Settings;

/// <summary>Typed readers for AppSetting rows, with safe defaults.</summary>
public static class AppSettingsProvider
{
    public const string NearbyRadiusKey = "NearbyRadiusKm";
    public const double DefaultNearbyRadiusKm = 30;

    /// <summary>The global "how far is nearby" cap (km) for home-screen serving.</summary>
    public static async Task<double> GetNearbyRadiusKmAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var raw = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == NearbyRadiusKey && !s.IsDeleted)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return raw is not null && double.TryParse(raw, out var km) && km > 0
            ? km
            : DefaultNearbyRadiusKm;
    }
}
