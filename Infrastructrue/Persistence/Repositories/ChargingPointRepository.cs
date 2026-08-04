using System.Security.Cryptography.Xml;
using System.Text.Json;
using Application.ChargingPoints.Queries;
using Application.ChargingPoints.Queries.GetChargingPointById;
using Application.ChargingPoints.Queries.GetChargingPointsPaged;
using Application.Common.Interfaces;
using Application.Common.Models;
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
            // Demo/review stations never appear in public discovery.
            "CP.IsTest = 0",
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
               CP.CreatedAt,
               CP.ModifiedAt,
               CAST(CASE WHEN UA.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasOwner," +
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

        return MapToListDtos(results);
    }

    public async Task<PagedResult<GetAllChargingPointsDto>> GetChargingPointsPaged(
        GetChargingPointsPagedRequest filter, int? userId, CancellationToken cancellationToken)
    {
        // Page-by-ids: filter/sort/count over ChargingPoint alone (no row-exploding
        // joins), then fetch full details for just the page's ids.
        var whereConditions = new List<string>
        {
            "CP.IsDeleted = 0",
            // Demo/review stations never appear in public discovery.
            "CP.IsTest = 0",
            "(UA.Id IS NULL OR UA.IsDeleted = 0)"
        };
        var parameters = new List<object>();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            whereConditions.Add(
                "(CP.Name LIKE {" + parameters.Count + "} OR CP.Address LIKE {" + parameters.Count +
                "} OR CP.CityName LIKE {" + parameters.Count + "})");
            parameters.Add($"%{filter.Search.Trim()}%");
        }

        if (filter.ChargerPointTypeId.HasValue)
        {
            whereConditions.Add("CP.ChargerPointTypeId = {" + parameters.Count + "}");
            parameters.Add(filter.ChargerPointTypeId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.CityName))
        {
            whereConditions.Add("CP.CityName LIKE {" + parameters.Count + "}");
            parameters.Add($"%{filter.CityName}%");
        }

        if (filter.StatusId.HasValue)
        {
            whereConditions.Add("CP.StatusId = {" + parameters.Count + "}");
            parameters.Add(filter.StatusId.Value);
        }

        if (filter.ChargerBrandId.HasValue)
        {
            whereConditions.Add(
                "EXISTS (SELECT 1 FROM dbo.ChargingPointChargerBrand CB WHERE CB.ChargingPointId = CP.Id AND CB.ChargerBrandId = {" +
                parameters.Count + "})");
            parameters.Add(filter.ChargerBrandId.Value);
        }

        if (filter.IsVerified.HasValue)
        {
            whereConditions.Add("CP.IsVerified = {" + parameters.Count + "}");
            parameters.Add(filter.IsVerified.Value);
        }

        if (filter.PlugTypeId.HasValue)
        {
            whereConditions.Add(
                "EXISTS (SELECT 1 FROM dbo.ChargingPlug PL WHERE PL.ChargingPointId = CP.Id AND PL.PlugTypeId = {" +
                parameters.Count + "} AND PL.IsDeleted = 0)");
            parameters.Add(filter.PlugTypeId.Value);
        }

        var whereClause = string.Join(" AND ", whereConditions);

        var orderBy = filter.Sort?.ToLowerInvariant() switch
        {
            "name_asc" => "CP.Name ASC",
            "name_desc" => "CP.Name DESC",
            "visitors_asc" => "CP.VisitorsCount ASC",
            "visitors_desc" => "CP.VisitorsCount DESC",
            "rating_asc" => "ISNULL(LR.AVGChargingPointRate, 0) ASC",
            "rating_desc" => "ISNULL(LR.AVGChargingPointRate, 0) DESC",
            _ => "CP.Id DESC"
        };

        var baseFrom = $@"
        FROM ChargingPoint CP
                 LEFT JOIN dbo.UserAccount UA ON UA.Id = CP.OwnerId
                 LEFT JOIN (SELECT ChargingPointId,
                                   MAX(AVGChargingPointRate) AS AVGChargingPointRate
                            FROM Rate WHERE IsDeleted = 0
                            GROUP BY ChargingPointId) LR ON CP.Id = LR.ChargingPointId
        WHERE {whereClause}";

        var totalCount = (await applicationDbContext.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*) AS [Value] {baseFrom}", parameters.ToArray())
                .ToListAsync(cancellationToken))
            .Single();

        var paging = "";
        var page = Math.Max(1, filter.Page ?? 1);
        int? pageSize = filter.PageSize.HasValue ? Math.Clamp(filter.PageSize.Value, 1, 500) : null;
        if (pageSize.HasValue)
            paging = $" OFFSET {(page - 1) * pageSize.Value} ROWS FETCH NEXT {pageSize.Value} ROWS ONLY";

        var ids = await applicationDbContext.Database
            .SqlQueryRaw<int>($"SELECT CP.Id AS [Value] {baseFrom} ORDER BY {orderBy}{paging}", parameters.ToArray())
            .ToListAsync(cancellationToken);

        if (ids.Count == 0)
            return new PagedResult<GetAllChargingPointsDto>([], totalCount, page, pageSize ?? totalCount);

        // Fetch full rows for the page's ids (ids are ints from our own query — safe to inline).
        var idList = string.Join(",", ids);
        var detailParams = new List<object>();
        var favoriteJoin = "";
        var favoriteSelect = " CAST(0 AS BIT) AS IsFavorite";
        if (userId.HasValue)
        {
            detailParams.Add(userId.Value);
            favoriteJoin =
                " LEFT JOIN dbo.UserFavoriteChargingPoint UFP ON CP.Id = UFP.ChargingPointId AND UFP.UserId = {0} AND UFP.IsDeleted = 0";
            favoriteSelect = " CAST(CASE WHEN UFP.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsFavorite";
        }

        var detailSql = $@"
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
               CP.CreatedAt,
               CP.ModifiedAt,
               CAST(CASE WHEN UA.Id IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS HasOwner,{favoriteSelect},
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
                 LEFT JOIN dbo.PartnerAgreement PA ON CP.Id = PA.ProviderId AND PA.ProviderType = 'ChargingPoint' AND PA.IsActive = 1 AND PA.IsDeleted = 0{favoriteJoin}
        WHERE CP.Id IN ({idList})";

        var results = await applicationDbContext.Database
            .SqlQueryRaw<ChargingPointsResult>(detailSql, detailParams.ToArray())
            .ToListAsync(cancellationToken);

        var dtos = MapToListDtos(results);

        // Preserve the sorted page order (grouping does not guarantee it).
        var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        dtos = dtos.OrderBy(d => order.GetValueOrDefault(d.Id, int.MaxValue)).ToList();

        return new PagedResult<GetAllChargingPointsDto>(dtos, totalCount, page, pageSize ?? totalCount);
    }

    private List<GetAllChargingPointsDto> MapToListDtos(List<ChargingPointsResult> results)
        => results.GroupBy(x => x.Id).Select(x =>
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
                    x.First().IsPartner,
                    x.First().HasOwner,
                    x.First().CreatedAt,
                    x.First().ModifiedAt
                ))
            .ToList();

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
               CP.CreatedAt,
               CP.ModifiedAt,
               CP.PremiumPaymentDate,
               CP.PremiumExpiresAt,
               UA.Id                              AS OwnerAccountId,
               UA.Name                            AS OwnerName,
               UA.Email                           AS OwnerEmail,
               UA.Phone                           AS OwnerAccountPhone," +
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

        // Charger brands with per-brand charger counts (many-to-many junction)
        var chargerBrands = await applicationDbContext.ChargingPointChargerBrands
            .AsNoTracking()
            .Where(x => x.ChargingPointId == id)
            .Select(x => new ChargerBrandSummary(x.ChargerBrandId, x.ChargerBrand.Name, x.Count))
            .ToListAsync(cancellationToken);

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
            firstResult.IsPartner,
            firstResult.PremiumPaymentDate,
            firstResult.PremiumExpiresAt,
            firstResult.OwnerAccountId,
            firstResult.OwnerName,
            firstResult.OwnerEmail,
            firstResult.OwnerAccountPhone,
            chargerBrands,
            firstResult.CreatedAt,
            firstResult.ModifiedAt
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
            // Owned by the user, OR the user is the active worker assigned to this charging point.
            "(CP.OwnerId = {0} OR CP.Id IN (" +
                "SELECT ProviderId FROM ProviderManager " +
                "WHERE ProviderType = 'ChargingPoint' AND UserId = {0} AND IsActive = 1 AND IsDeleted = 0))"
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
               CP.CreatedAt,
               CP.ModifiedAt,
               CAST(1 AS BIT) AS HasOwner, -- owner-scoped query: rows always belong to the caller
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

        var dtos = MapToListDtos(results);

        // Owner surface: favorites count + the promo creative and its review status.
        var ids = dtos.Select(d => d.Id).ToList();
        if (ids.Count > 0)
        {
            var favCounts = await applicationDbContext.UserFavoriteChargingPoints
                .AsNoTracking()
                .Where(f => !f.IsDeleted && ids.Contains(f.ChargingPointId))
                .GroupBy(f => f.ChargingPointId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

            var viewImages = await applicationDbContext.ChargingPoints
                .AsNoTracking()
                .Where(c => ids.Contains(c.Id) && c.ViewImage != null)
                .Select(c => new { c.Id, c.ViewImage, c.ViewImageStatus })
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            dtos = dtos.Select(d => d with
            {
                FavoritesCount = favCounts.GetValueOrDefault(d.Id, 0),
                ViewImage = viewImages.TryGetValue(d.Id, out var vi) && vi.ViewImage != null
                    ? uploadFileService.GetFilePath(UploadFileFolders.CableViewImages, vi.ViewImage)
                    : null,
                ViewImageStatus = viewImages.TryGetValue(d.Id, out var vs) ? vs.ViewImageStatus : null
            }).ToList();
        }

        return dtos;
    }
}