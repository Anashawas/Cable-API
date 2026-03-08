namespace Cable.Requests.Partners;

public record CreatePartnerAgreementRequest(
    string ProviderType,
    int ProviderId,
    double CommissionPercentage,
    double PointsRewardPercentage,
    int? PointsConversionRateId,
    int CodeExpiryMinutes,
    string? Note
);
