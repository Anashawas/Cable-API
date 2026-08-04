namespace Application.Loyalty;

public record AdjustmentReasonDto(string Code, string Name);

/// <summary>
/// K2 — static catalog of reason codes for admin point adjustments.
/// Kept in code (no table) until the business asks to manage them dynamically.
/// </summary>
public static class AdjustmentReasons
{
    public static readonly IReadOnlyList<AdjustmentReasonDto> All =
    [
        new("COMPENSATION",     "Customer compensation"),
        new("CORRECTION",       "Data / balance correction"),
        new("PROMOTION",        "Promotional award"),
        new("CAMPAIGN",         "Marketing campaign"),
        new("FRAUD_CLAWBACK",   "Fraud clawback"),
        new("SUPPORT_GOODWILL", "Support goodwill gesture"),
        new("OTHER",            "Other (see note)")
    ];

    public static bool IsValid(string code) => All.Any(r => r.Code == code);
}
