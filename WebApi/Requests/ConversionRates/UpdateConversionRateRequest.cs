namespace Cable.Requests.ConversionRates;

public record UpdateConversionRateRequest(
    string Name,
    string CurrencyCode,
    double PointsPerUnit,
    bool IsDefault,
    bool IsActive
);
