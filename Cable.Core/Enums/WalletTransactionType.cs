namespace Cable.Core.Emuns;

public enum WalletTransactionType
{
    Deposit = 1,
    SettlementDeduction = 2,
    Refund = 3,
    Adjustment = 4,
    CommissionDeduction = 5,
    CommissionRefund = 6,
    OfferPaymentCredit = 7,
    OfferPaymentRefund = 8
}
