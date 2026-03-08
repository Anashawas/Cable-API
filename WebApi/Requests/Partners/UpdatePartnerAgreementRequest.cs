namespace Cable.Requests.Partners;

public record UpdatePartnerAgreementRequest(
    double CommissionPercentage,
    double PointsRewardPercentage,
    int? PointsConversionRateId,
    int CodeExpiryMinutes,
    string? Note,
    bool IsActive
);
