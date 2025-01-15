using Application.Common.Interfaces.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Persistence.Repositories;

public class RateRepository(ApplicationDbContext applicationDbContext) : IRateRepository
{
    public async Task<double> CalculateChargePointAverageRate(int chargingPointId, int chargingPointRate,
        CancellationToken cancellationToken = default)
    {
        var query = @"
    SELECT COALESCE(SUM(ChargingPointRate), 0) AS TotalRate, 
           COUNT(*) AS TotalCount
    FROM Rates
    WHERE ChargingPointId = @ChargingPointId AND IsDeleted = 0";

        await using (var command = applicationDbContext.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = query;
            command.Parameters.Add(new SqlParameter("@ChargingPointId", chargingPointId));

            await applicationDbContext.Database.OpenConnectionAsync(cancellationToken);

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) return chargingPointRate;
                int totalRate = reader.GetInt32(0);
                int totalCount = reader.GetInt32(1);

                if (totalCount == 0)
                {
                    return chargingPointRate;
                }

                var newSum = totalRate + chargingPointRate;
                var newCount = totalCount + 1;

                return (double)newSum / newCount;
            }
        }
    }

    public async Task<double> GetChargingPointRateAverage(int chargingPointId, CancellationToken cancellationToken = default) 
        => await applicationDbContext.Rates
            .FromSqlInterpolated($@"
        SELECT TOP 1 AVGChargingPointRate
        FROM Rate
        WHERE ChargingPointId = {chargingPointId} AND IsDeleted = 0
        ORDER BY CreatedAt DESC")
            .AsNoTracking()
            .Select(x => x.AVGChargingPointRate)
            .FirstOrDefaultAsync(cancellationToken);
       

    
}