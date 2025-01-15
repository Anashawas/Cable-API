namespace Application.Common.Interfaces.Repositories;

public interface IRateRepository
{
    Task<double> CalculateChargePointAverageRate(int chargingPointId, int chargingPointRate,
        CancellationToken cancellationToken = default);
    
    
    Task<double>GetChargingPointRateAverage(int chargingPointId, CancellationToken cancellationToken = default);
}