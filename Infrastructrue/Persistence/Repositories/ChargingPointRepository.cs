using System.Security.Cryptography.Xml;
using System.Text.Json;
using Application.ChargingPoints.Queries;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Favorites.Queries.GetUserFavorites;
using Cable.Core.Emuns;
using Cable.Core.Exceptions;
using Domain.Enitites;
using Infrastructrue.Common.Models.Results.ChargingPoints;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Persistence.Repositories;

public class ChargingPointRepository(ApplicationDbContext applicationDbContext, IUploadFileService uploadFileService)
    : IChargingPointRepository
{
    public async Task<List<GetAllChargingPointsDto>> GetAllChargingPoints(int? chargerPointTypeId, string? cityName, int? userId,
        CancellationToken cancellationToken)
    {
        var whereConditions = new List<string>
        {
            "CP.IsDeleted = 0",
            "(UA.Id IS NULL OR UA.IsDeleted = 0)"
        };

        var parameters = new List<object>();

        if (!string.IsNullOrEmpty(cityName))
        {
            whereConditions.Add("CP.CityName LIKE {" + parameters.Count + "}");
            parameters.Add($"%{cityName}%");
        }

        if (chargerPointTypeId.HasValue)
        {
            whereConditions.Add("CP.ChargerPointTypeId = {" + parameters.Count + "}");
            parameters.Add(chargerPointTypeId.Value);
        }

        var userIdParameterIndex = parameters.Count;
        if (userId.HasValue)
        {
            parameters.Add(userId.Value);
        }

        var whereClause = string.Join(" AND ", whereConditions);

        var sql = $@"
        WITH LatestRatings AS (
            SELECT R.Id,
                   ROW_NUMBER() OVER (PARTITION BY ChargingPointId ORDER BY CreatedAt DESC) AS RateRowNumber,
                   COUNT(*) OVER (PARTITION BY ChargingPointId) AS RateCount,
                   AVGChargingPointRate,
                   ChargingPointId
            FROM Rate R
            WHERE R.IsDeleted = 0
        )
        SELECT CP.Id,
               CP.Name,
               CP.Address,
               CP.Phone,
               CP.OwnerPhone,
               CP.FromTime,
               CP.ToTime,
               CP.Latitude,
               CP.Longitude,
               ISNULL(LR.AVGChargingPointRate, 0) AS AvgChargingPointRate,
               ISNULL(LR.RateCount, 0) AS RateCount,
               S.Id AS StatusId,
               S.Name AS StatusName,
               PT.ID AS PlugTypeId,
               PT.Name AS PlugTypeName,
               PT.SerialNumber AS SerialNumber,
               CPT.Id AS ChargingPointTypeId,
               CPT.Name AS ChargingPointTypeName,
               ST.Id AS StationTypeId,
               ST.Name AS StationTypeName,
               CP.CityName,
               CP.CountryName,
               CP.IsVerified,
               CP.price,
               CP.ChargerSpeed,
               CP.ChargersCount,
               CP.VisitorsCount,
               CP.HasOffer,
               CP.Service,
               CP.OfferDescription,
               CP.Note,
               CP.Icon,
               CPA.FileName,
               CP.MethodPayment,
               CP.ChargerBrand," +
               (userId.HasValue ? " CAST(CASE WHEN UFP.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsFavorite" : " CAST(0 AS BIT) AS IsFavorite") + $@",
               CAST(CASE WHEN PA.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsPartner
        FROM ChargingPoint CP
                 LEFT JOIN LatestRatings LR ON CP.Id = LR.ChargingPointId AND LR.RateRowNumber = 1
                 LEFT JOIN dbo.UserAccount UA ON UA.Id = CP.OwnerId
                 LEFT JOIN dbo.Status S ON S.Id = CP.StatusId
                 LEFT JOIN dbo.ChargingPointType CPT ON CP.ChargerPointTypeId = CPT.Id
                 LEFT JOIN dbo.StationType ST ON CP.StationTypeId = ST.Id
                 LEFT JOIN ChargingPointAttachment CPA ON CP.Id = CPA.ChargingPointId
                 LEFT JOIN dbo.ChargingPlug C ON CP.Id = C.ChargingPointId
                 LEFT JOIN dbo.PlugType PT ON C.PlugTypeId = PT.Id
                 LEFT JOIN dbo.PartnerAgreement PA ON CP.Id = PA.ProviderId AND PA.ProviderType = 'ChargingPoint' AND PA.IsActive = 1 AND PA.IsDeleted = 0" +
                 (userId.HasValue ? $" LEFT JOIN dbo.UserFavoriteChargingPoint UFP ON CP.Id = UFP.ChargingPointId AND UFP.UserId = {{{userIdParameterIndex}}} AND UFP.IsDeleted = 0" : "") + $@"
        WHERE {whereClause}";


        var results = await applicationDbContext.Database
            .SqlQueryRaw<ChargingPointsResult>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);

        var chargingPoints = results.GroupBy(x => x.Id).Select(x =>
                new GetAllChargingPointsDto(x.First().Id, x.First().Name, x.First().CityName, x.First().CountryName,
                    x.First().Phone, x.First().OwnerPhone, x.First().FromTime, x.First().ToTime,
                    x.First().Latitude,
                    x.First().Longitude,
                    x.First().IsVerified,
                    x.First().HasOffer,
                    x.First().Service,
                    x.First().OfferDescription,
                    x.First().Address,
                    x.First().AvgChargingPointRate,
                    !string.IsNullOrEmpty(x.First().Icon)
                        ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, x.First().Icon)
                        : null,
                    x.First().RateCount,
                    x.First().Price,
                    x.First().ChargerSpeed,
                    x.First().ChargersCount,
                    x.First().VisitorsCount,
                    x.First().Note,
                    x.First().MethodPayment,
                    x.First().ChargerBrand,
                    new StatusSummary(x.First().StatusId, x.First().StatusName),
                    x.First().ChargingPointTypeId > 0 && !string.IsNullOrEmpty(x.First().ChargingPointTypeName)
                        ? new ChargingPointTypeSummary(x.First().ChargingPointTypeId, x.First().ChargingPointTypeName)
                        : null,
                    x.First().StationTypeId > 0 && !string.IsNullOrEmpty(x.First().StationTypeName)
                        ? new StationTypeSummary(x.First().StationTypeId, x.First().StationTypeName)
                        : null,
                    x.Where(w => !string.IsNullOrEmpty(w.FileName))
                        .Select(w => uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, w.FileName!))
                        .Distinct()
                        .ToList(),
                    x.Where(w=>w.PlugTypeId != null && !string.IsNullOrEmpty(w.PlugTypeName)).GroupBy(z=>z.PlugTypeId).SelectMany(y=>
                        y.Select(r=>new PlugTypeSummary(r.PlugTypeId!.Value, r.PlugTypeName ?? "", r.SerialNumber ?? ""))).ToList(),
                    x.First().IsFavorite,
                    x.First().IsPartner
                ))
            .ToList();

        return chargingPoints;
    }

    public async Task<GetChargingPointByIdDto> GetChargingPointById(int id, int? userId, CancellationToken cancellationToken)
    {
        var parameters = new List<object> { id };

        if (userId.HasValue)
        {
            parameters.Add(userId.Value);
        }

        var sql = @"
        WITH LatestRatings AS (SELECT R.Id,
                                      ROW_NUMBER() OVER (PARTITION BY ChargingPointId ORDER BY CreatedAt DESC) AS RateRowNumber,
                                      COUNT(*) OVER (PARTITION BY ChargingPointId)                             AS RateCount,
                                      AVGChargingPointRate,
                                      ChargingPointId
                               FROM Rate R
                               WHERE R.IsDeleted = 0)
        SELECT CP.Id,
               CP.Name,
               CP.Address,
               CP.Phone,
               CP.OwnerPhone,
               CP.FromTime,
               CP.ToTime,
               CP.Latitude,
               CP.Longitude,
               ISNULL(LR.AVGChargingPointRate, 0) AS AvgChargingPointRate,
               ISNULL(LR.RateCount, 0)            AS RateCount,
               S.Id                               AS StatusId,
               S.Name                             AS StatusName,
               PT.ID                              AS PlugTypeId,
               PT.Name                            AS PlugTypeName,
               PT.SerialNumber                    AS SerialNumber,
               CPT.Id                             AS ChargingPointTypeId,
               CPT.Name                           AS ChargingPointTypeName,
               ST.Id                              AS StationTypeId,
               ST.Name                            AS StationTypeName,
               CP.CityName,
               CP.CountryName,
               CP.IsVerified,
               CP.price,
               CP.ChargerSpeed,
               CP.ChargersCount,
               CP.VisitorsCount,
               CP.HasOffer,
               CP.Service,
               CP.OfferDescription,
               CP.Note,
               CP.Icon,
               CPA.FileName,
               CP.MethodPayment,
               CP.ChargerBrand," +
               (userId.HasValue ? " CAST(CASE WHEN UFP.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsFavorite" : " CAST(0 AS BIT) AS IsFavorite") + @",
               CAST(CASE WHEN PA.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsPartner
        FROM ChargingPoint CP
                 LEFT JOIN LatestRatings LR ON CP.Id = LR.ChargingPointId AND LR.RateRowNumber = 1
                 LEFT JOIN dbo.UserAccount UA ON UA.Id = CP.OwnerId
                 LEFT JOIN dbo.Status S ON S.Id = CP.StatusId
                 LEFT JOIN dbo.ChargingPointType CPT ON CP.ChargerPointTypeId = CPT.Id
                 LEFT JOIN dbo.StationType ST ON CP.StationTypeId = ST.Id
                 LEFT JOIN ChargingPointAttachment CPA ON CP.Id = CPA.ChargingPointId
                 LEFT JOIN dbo.ChargingPlug C ON CP.Id = C.ChargingPointId
                 LEFT JOIN dbo.PlugType PT ON C.PlugTypeId = PT.Id
                 LEFT JOIN dbo.PartnerAgreement PA ON CP.Id = PA.ProviderId AND PA.ProviderType = 'ChargingPoint' AND PA.IsActive = 1 AND PA.IsDeleted = 0" +
                 (userId.HasValue ? " LEFT JOIN dbo.UserFavoriteChargingPoint UFP ON CP.Id = UFP.ChargingPointId AND UFP.UserId = {1} AND UFP.IsDeleted = 0" : "") + @"
        WHERE CP.IsDeleted = 0 AND CP.Id = {0}
          AND (UA.Id IS NULL OR UA.IsDeleted = 0);";

        var results = await applicationDbContext.Database
            .SqlQueryRaw<ChargingPointResult>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);

        if (!results.Any())
            throw new NotFoundException(nameof(ChargingPoint), id);

        var firstResult = results.First();

        var attachmentFileNames = results
            .Where(r => !string.IsNullOrEmpty(r.FileName))
            .Select(r => r.FileName)
            .Distinct()
            .ToList();

        var plugTypes = results
            .Where(r => r.PlugTypeId.HasValue && !string.IsNullOrEmpty(r.PlugTypeName))
            .GroupBy(r => r.PlugTypeId)
            .Select(g => new PlugTypeSummary(
                g.Key!.Value,
                g.First().PlugTypeName ??"",
                g.First().SerialNumber ??""
            ))
            .ToList();

        return new GetChargingPointByIdDto(
            firstResult.Id,
            firstResult.Name,
            firstResult.CityName,
            firstResult.CountryName,
            firstResult.Phone,
            firstResult.OwnerPhone,
            firstResult.FromTime,
            firstResult.ToTime,
            firstResult.Latitude,
            firstResult.Longitude,
            firstResult.IsVerified,
            firstResult.HasOffer,
            firstResult.Service,
            firstResult.OfferDescription,
            firstResult.Address,
            firstResult.AvgChargingPointRate,
            !string.IsNullOrEmpty(firstResult.Icon)
                ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, firstResult.Icon)
                : null,
            firstResult.RateCount,
            firstResult.Price,
            firstResult.ChargerSpeed,
            firstResult.ChargersCount,
            firstResult.VisitorsCount,
            firstResult.Note,
            firstResult.MethodPayment,
            firstResult.ChargerBrand,
            new StatusSummary(firstResult.StatusId, firstResult.StatusName),
            firstResult.ChargingPointTypeId > 0 && !string.IsNullOrEmpty(firstResult.ChargingPointTypeName)
                ? new ChargingPointTypeSummary(firstResult.ChargingPointTypeId, firstResult.ChargingPointTypeName)
                : null,
            firstResult.StationTypeId > 0 && !string.IsNullOrEmpty(firstResult.StationTypeName)
                ? new StationTypeSummary(firstResult.StationTypeId, firstResult.StationTypeName)
                : null,
            attachmentFileNames.Select(fileName =>
                uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, fileName!)).ToList(),
            plugTypes,
            firstResult.IsFavorite,
            firstResult.IsPartner
        );
    }

    public async Task<List<GetUserFavoritesDto>> GetUserFavoriteChargingPoints(int userId,
        CancellationToken cancellationToken)
    {
        var parameters = (List<object>)[userId];

        var sql = @"
        WITH LatestRatings AS (
            SELECT R.Id,
                   ROW_NUMBER() OVER (PARTITION BY ChargingPointId ORDER BY CreatedAt DESC) AS RateRowNumber,
                   COUNT(*) OVER (PARTITION BY ChargingPointId) AS RateCount,
                   AVGChargingPointRate,
                   ChargingPointId
            FROM Rate R
            WHERE R.IsDeleted = 0
        )
        SELECT UFP.Id AS FavoriteId,
               UFP.CreatedAt AS AddedToFavoritesAt,
               CP.Id,
               CP.Name,
               CP.Address,
               CP.Phone,
               CP.OwnerPhone,
               CP.FromTime,
               CP.ToTime,
               CP.Latitude,
               CP.Longitude,
               ISNULL(LR.AVGChargingPointRate, 0) AS AvgChargingPointRate,
               ISNULL(LR.RateCount, 0) AS RateCount,
               S.Id AS StatusId,
               S.Name AS StatusName,
               PT.ID AS PlugTypeId,
               PT.Name AS PlugTypeName,
               PT.SerialNumber AS SerialNumber,
               CPT.Id AS ChargingPointTypeId,
               CPT.Name AS ChargingPointTypeName,
               ST.Id AS StationTypeId,
               ST.Name AS StationTypeName,
               CP.CityName,
               CP.CountryName,
               CP.IsVerified,
               CP.price,
               CP.ChargerSpeed,
               CP.ChargersCount,
               CP.VisitorsCount,
               CP.HasOffer,
               CP.Service,
               CP.OfferDescription,
               CP.Note,
               CP.Icon,
               CPA.FileName,
               CP.MethodPayment
        FROM UserFavoriteChargingPoint UFP
                 INNER JOIN ChargingPoint CP ON UFP.ChargingPointId = CP.Id AND CP.IsDeleted = 0
                 LEFT JOIN LatestRatings LR ON CP.Id = LR.ChargingPointId AND LR.RateRowNumber = 1
                 LEFT JOIN dbo.UserAccount UA ON UA.Id = CP.OwnerId
                 LEFT JOIN dbo.Status S ON S.Id = CP.StatusId
                 LEFT JOIN dbo.ChargingPointType CPT ON CP.ChargerPointTypeId = CPT.Id
                 LEFT JOIN dbo.StationType ST ON CP.StationTypeId = ST.Id
                 LEFT JOIN ChargingPointAttachment CPA ON CP.Id = CPA.ChargingPointId AND CPA.IsDeleted = 0
                 LEFT JOIN dbo.ChargingPlug C ON CP.Id = C.ChargingPointId AND C.IsDeleted = 0
                 LEFT JOIN dbo.PlugType PT ON C.PlugTypeId = PT.Id
        WHERE UFP.IsDeleted = 0
          AND UFP.UserId = {0}
          AND (UA.Id IS NULL OR UA.IsDeleted = 0)
        ORDER BY UFP.CreatedAt DESC";

        var results = await applicationDbContext.Database
            .SqlQueryRaw<UserFavoriteChargingPointResult>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);

        var favorites = results.GroupBy(x => x.Id).Select(x =>
            new GetUserFavoritesDto(
                FavoriteId: x.First().FavoriteId,
                ChargingPointId: x.First().Id,
                Name: x.First().Name,
                Address: x.First().Address,
                CityName: x.First().CityName,
                CountryName: x.First().CountryName,
                Latitude: x.First().Latitude,
                Longitude: x.First().Longitude,
                Phone: x.First().Phone,
                Price: x.First().Price,
                FromTime: x.First().FromTime,
                ToTime: x.First().ToTime,
                IsVerified: x.First().IsVerified,
                VisitorsCount: x.First().VisitorsCount ?? 0,
                Icon: !string.IsNullOrEmpty(x.First().Icon)
                    ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, x.First().Icon)
                    : null,
                HasOffer: x.First().HasOffer,
                OfferDescription: x.First().OfferDescription,
                AvgRating: x.First().AvgChargingPointRate ?? 0,
                RateCount: x.First().RateCount,
                Status: new StatusSummary(x.First().StatusId, x.First().StatusName),
                ChargingPointType: x.First().ChargingPointTypeId > 0 && !string.IsNullOrEmpty(x.First().ChargingPointTypeName)
                    ? new ChargingPointTypeSummary(x.First().ChargingPointTypeId, x.First().ChargingPointTypeName)
                    : null,
                PlugTypes: x.Where(w => w.PlugTypeId != null && !string.IsNullOrEmpty(w.PlugTypeName))
                    .GroupBy(z => z.PlugTypeId)
                    .SelectMany(y => y.Select(r => new PlugTypeSummary(r.PlugTypeId!.Value, r.PlugTypeName ?? "", r.SerialNumber ?? "")))
                    .ToList(),
                Images: x.Where(w => !string.IsNullOrEmpty(w.FileName))
                    .Select(w => uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, w.FileName!))
                    .Distinct()
                    .ToList(),
                AddedToFavoritesAt: x.First().AddedToFavoritesAt
            )).ToList();

        return favorites;
    }

    public async Task<List<GetAllChargingPointsDto>> GetChargingPointsByOwner(int ownerId, int? chargerPointTypeId, string? cityName, CancellationToken cancellationToken)
    {
        var whereConditions = new List<string>
        {
            "CP.IsDeleted = 0",
            "CP.OwnerId = {0}"
        };

        var parameters = new List<object> { ownerId };

        if (!string.IsNullOrEmpty(cityName))
        {
            whereConditions.Add("CP.CityName LIKE {" + parameters.Count + "}");
            parameters.Add($"%{cityName}%");
        }

        if (chargerPointTypeId.HasValue)
        {
            whereConditions.Add("CP.ChargerPointTypeId = {" + parameters.Count + "}");
            parameters.Add(chargerPointTypeId.Value);
        }

        var whereClause = string.Join(" AND ", whereConditions);

        var sql = $@"
        WITH LatestRatings AS (
            SELECT R.Id,
                   ROW_NUMBER() OVER (PARTITION BY ChargingPointId ORDER BY CreatedAt DESC) AS RateRowNumber,
                   COUNT(*) OVER (PARTITION BY ChargingPointId) AS RateCount,
                   AVGChargingPointRate,
                   ChargingPointId
            FROM Rate R
            WHERE R.IsDeleted = 0
        )
        SELECT CP.Id,
               CP.Name,
               CP.Address,
               CP.Phone,
               CP.OwnerPhone,
               CP.FromTime,
               CP.ToTime,
               CP.Latitude,
               CP.Longitude,
               ISNULL(LR.AVGChargingPointRate, 0) AS AvgChargingPointRate,
               ISNULL(LR.RateCount, 0) AS RateCount,
               S.Id AS StatusId,
               S.Name AS StatusName,
               PT.ID AS PlugTypeId,
               PT.Name AS PlugTypeName,
               PT.SerialNumber AS SerialNumber,
               CPT.Id AS ChargingPointTypeId,
               CPT.Name AS ChargingPointTypeName,
               ST.Id AS StationTypeId,
               ST.Name AS StationTypeName,
               CP.CityName,
               CP.CountryName,
               CP.IsVerified,
               CP.price,
               CP.ChargerSpeed,
               CP.ChargersCount,
               CP.VisitorsCount,
               CP.HasOffer,
               CP.Service,
               CP.OfferDescription,
               CP.Note,
               CP.Icon,
               CPA.FileName,
               CP.MethodPayment,
               CP.ChargerBrand,
               CAST(0 AS BIT) AS IsFavorite,
               CAST(CASE WHEN PA.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsPartner
        FROM ChargingPoint CP
                 LEFT JOIN LatestRatings LR ON CP.Id = LR.ChargingPointId AND LR.RateRowNumber = 1
                 LEFT JOIN dbo.Status S ON S.Id = CP.StatusId
                 LEFT JOIN dbo.ChargingPointType CPT ON CP.ChargerPointTypeId = CPT.Id
                 LEFT JOIN dbo.StationType ST ON CP.StationTypeId = ST.Id
                 LEFT JOIN ChargingPointAttachment CPA ON CP.Id = CPA.ChargingPointId
                 LEFT JOIN dbo.ChargingPlug C ON CP.Id = C.ChargingPointId
                 LEFT JOIN dbo.PlugType PT ON C.PlugTypeId = PT.Id
                 LEFT JOIN dbo.PartnerAgreement PA ON CP.Id = PA.ProviderId AND PA.ProviderType = 'ChargingPoint' AND PA.IsActive = 1 AND PA.IsDeleted = 0
        WHERE {whereClause}";

        var results = await applicationDbContext.Database
            .SqlQueryRaw<ChargingPointsResult>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);

        var chargingPoints = results.GroupBy(x => x.Id).Select(x =>
                new GetAllChargingPointsDto(x.First().Id, x.First().Name, x.First().CityName, x.First().CountryName,
                    x.First().Phone, x.First().OwnerPhone, x.First().FromTime, x.First().ToTime,
                    x.First().Latitude,
                    x.First().Longitude,
                    x.First().IsVerified,
                    x.First().HasOffer,
                    x.First().Service,
                    x.First().OfferDescription,
                    x.First().Address,
                    x.First().AvgChargingPointRate,
                    !string.IsNullOrEmpty(x.First().Icon)
                        ? uploadFileService.GetFilePath(UploadFileFolders.CableChargingPoint, x.First().Icon)
                        : null,
                    x.First().RateCount,
                    x.First().Price,
                    x.First().ChargerSpeed,
                    x.First().ChargersCount,
                    x.First().VisitorsCount,
                    x.First().Note,
                    x.First().MethodPayment,
                    x.First().ChargerBrand,
                    new StatusSummary(x.First().StatusId, x.First().StatusName),
                    x.First().ChargingPointTypeId > 0 && !string.IsNullOrEmpty(x.First().ChargingPointTypeName)
                        ? new ChargingPointTypeSummary(x.First().ChargingPointTypeId, x.First().ChargingPointTypeName)
                        : null,
                    x.First().StationTypeId > 0 && !string.IsNullOrEmpty(x.First().StationTypeName)
                        ? new StationTypeSummary(x.First().StationTypeId, x.First().StationTypeName)
                        : null,
                    x.Where(w => !string.IsNullOrEmpty(w.FileName))
                        .Select(w => uploadFileService.GetFilePath(UploadFileFolders.CableAttachments, w.FileName!))
                        .Distinct()
                        .ToList(),
                    x.Where(w=>w.PlugTypeId != null && !string.IsNullOrEmpty(w.PlugTypeName)).GroupBy(z=>z.PlugTypeId).SelectMany(y=>
                        y.Select(r=>new PlugTypeSummary(r.PlugTypeId!.Value, r.PlugTypeName ?? "", r.SerialNumber ?? ""))).ToList(),
                    x.First().IsFavorite,
                    x.First().IsPartner
                ))
            .ToList();

        return chargingPoints;
    }
}