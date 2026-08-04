using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common;
using Application.Common.Interfaces;
using Application.Settings;
using Cable.Core;
using Cable.Core.Emuns;
using Microsoft.EntityFrameworkCore;

namespace Application.ChargingPoints.Queries.GetNearestStations;

public record NearestStationDto(
    int Id,
    string Name,
    double Latitude,
    double Longitude,
    string? CityName,
    double DistanceKm,
    bool IsPremium,
    DateTime? PremiumUntil,
    string? ViewImage,
    string? IConUrl,
    bool IsVerified,
    bool IsPartner,
    StatusSummary StatusSummary,
    StationTypeSummary? StationType);

/// <summary>
/// Nearest stations to the caller for the home screen, within the admin
/// nearby-radius, sorted by distance only (stations are never priority-
/// weighted). premium filter: "true" (paid stations), "false", or "all".
/// isPremium derives from the paid-premium dates (warn-only expiry: an expired
/// premium stays premium until an admin removes it).
/// </summary>
public record GetNearestStationsRequest(double Lat, double Lng, string Premium = "all")
    : IRequest<List<NearestStationDto>>;

public class GetNearestStationsRequestHandler(
    IApplicationDbContext applicationDbContext,
    IUploadFileService uploadFileService)
    : IRequestHandler<GetNearestStationsRequest, List<NearestStationDto>>
{
    public async Task<List<NearestStationDto>> Handle(GetNearestStationsRequest request,
        CancellationToken cancellationToken)
    {
        var premiumFilter = request.Premium?.Trim().ToLowerInvariant() ?? "all";
        if (premiumFilter is not ("true" or "false" or "all"))
            throw new DataValidationException("Premium", "premium must be 'true', 'false' or 'all'");

        var radiusKm = await AppSettingsProvider.GetNearbyRadiusKmAsync(applicationDbContext, cancellationToken);

        var candidates = await applicationDbContext.ChargingPoints.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsTest)
            .Select(x => new
            {
                x.Id, x.Name, x.Latitude, x.Longitude, x.CityName, x.Icon, x.IsVerified,
                x.StatusId, x.StationTypeId, x.PremiumPaymentDate, x.PremiumExpiresAt,
                x.ViewImage, x.ViewImageStatus
            })
            .ToListAsync(cancellationToken);

        var nearby = candidates
            .Select(x => new { Station = x, DistanceKm = GeoDistance.Km(request.Lat, request.Lng, x.Latitude, x.Longitude) })
            .Where(x => x.DistanceKm <= radiusKm)
            .Where(x => premiumFilter switch
            {
                "true" => x.Station.PremiumPaymentDate != null,
                "false" => x.Station.PremiumPaymentDate == null,
                _ => true
            })
            .OrderBy(x => x.DistanceKm)
            .ToList();

        if (nearby.Count == 0)
            return [];

        var nearbyIds = nearby.Select(x => x.Station.Id).ToList();

        var statusNames = await applicationDbContext.Statuses.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var stationTypeNames = await applicationDbContext.StationTypes.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var partnerIds = (await applicationDbContext.PartnerAgreements.AsNoTracking()
                .Where(pa => pa.ProviderType == "ChargingPoint" && pa.IsActive && !pa.IsDeleted
                             && nearbyIds.Contains(pa.ProviderId))
                .Select(pa => pa.ProviderId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return nearby.Select(x => new NearestStationDto(
            x.Station.Id,
            x.Station.Name,
            x.Station.Latitude,
            x.Station.Longitude,
            x.Station.CityName,
            Math.Round(x.DistanceKm, 2),
            x.Station.PremiumPaymentDate != null,
            x.Station.PremiumExpiresAt,
            // Only the ADMIN-APPROVED creative is ever served to B2C.
            x.Station.ViewImageStatus == "approved" && !string.IsNullOrEmpty(x.Station.ViewImage)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableViewImages, x.Station.ViewImage)
                : null,
            !string.IsNullOrEmpty(x.Station.Icon)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, x.Station.Icon)
                : null,
            x.Station.IsVerified,
            partnerIds.Contains(x.Station.Id),
            new StatusSummary(x.Station.StatusId, statusNames.GetValueOrDefault(x.Station.StatusId, "")),
            stationTypeNames.TryGetValue(x.Station.StationTypeId, out var stName)
                ? new StationTypeSummary(x.Station.StationTypeId, stName)
                : null
        )).ToList();
    }
}
