using Domain.Common;

namespace Domain.Enitites;

public class ProviderSettlement : BaseAuditableEntity
{
    public string ProviderType { get; set; } = null!;
    public int ProviderId { get; set; }
    public int ProviderOwnerId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }

    // Partner Transaction aggregates (user pays provider for EV charging; Cable takes commission)
    public int PartnerTransactionCount { get; set; }
    public decimal PartnerTransactionAmount { get; set; }
    public decimal PartnerCommissionAmount { get; set; }
    public int TotalPointsAwarded { get; set; }

    // Offer Transaction aggregates (user redeems loyalty points at provider; Cable pays provider)
    public int OfferTransactionCount { get; set; }
    public decimal OfferPaymentAmount { get; set; }
    public int TotalPointsDeducted { get; set; }

    // Net settlement: (PartnerTransactionAmount - PartnerCommissionAmount) + OfferPaymentAmount
    public decimal NetAmountDueToProvider { get; set; }

    public int SettlementStatus { get; set; }
    public DateTime? InvoicedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal? PaidAmount { get; set; }
    public string? AdminNote { get; set; }

    public virtual UserAccount ProviderOwner { get; set; } = null!;
}
