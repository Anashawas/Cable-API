namespace Cable.Core.Enums;

/// <summary>What happens to a subscription after its expiry date passes.</summary>
public enum SubscriptionGraceMode
{
    /// <summary>Stays on until an admin switches it off. The shipped default.</summary>
    Manual = 1,
    /// <summary>The daily job switches it off GraceDays after expiry.</summary>
    AfterDays = 2
}
