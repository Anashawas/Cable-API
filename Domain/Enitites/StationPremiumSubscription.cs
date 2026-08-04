using System;
using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// One row per premium payment/renewal for a charging point — the immutable
/// payment history. The latest PaymentDate/ExpiresAt are denormalized onto
/// <see cref="ChargingPoint.PremiumPaymentDate"/> / <see cref="ChargingPoint.PremiumExpiresAt"/>
/// for fast reads.
/// </summary>
public class StationPremiumSubscription : BaseAuditableEntity
{
    public int ChargingPointId { get; set; }

    /// <summary>When the owner paid for premium placement.</summary>
    public DateTime PaymentDate { get; set; }

    /// <summary>When this premium period ends.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Amount paid for this period.</summary>
    public decimal? Amount { get; set; }

    public string? Note { get; set; }

    public virtual ChargingPoint ChargingPoint { get; set; } = null!;
}
