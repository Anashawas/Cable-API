namespace Cable.Requests.ConversionRates;

public record CreateConversionRateRequest(
    string Name,
    string CurrencyCode,
    double PointsPerUnit,
    bool IsDefault,
    bool IsActive
);
