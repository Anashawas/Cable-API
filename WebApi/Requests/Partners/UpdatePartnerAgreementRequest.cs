namespace Cable.Requests.Partners;

public record UpdatePartnerAgreementRequest(
    double CommissionPercentage,
    double PointsRewardPercentage,
    int? PointsConversionRateId,
    int CodeExpirySeconds,
    decimal? MinimumTransactionAmount,
    string? Note,
    bool IsActive
);
