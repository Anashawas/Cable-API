using Domain.Common;

namespace Domain.Enitites;

/// <summary>
/// One offline payment (CliQ / cash) against a <see cref="Subscription"/>. The
/// immutable money record: never deleted, only voided, so the audit trail and
/// the monthly totals stay honest.
///
/// <see cref="PeriodStart"/>/<see cref="PeriodEnd"/> are the days this payment
/// bought. They are stored per payment — not derived from the subscription — so
/// voiding the latest payment can roll the subscription's expiry back to the
/// previous one without guesswork.
///
/// Who recorded it and when is <c>CreatedBy</c> / <c>CreatedAt</c> from the base
/// entity; no separate recordedBy column.
/// </summary>
public class Payment : BaseAuditableEntity
{
    public int SubscriptionId { get; set; }
    public int PayerId { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "JOD";
    /// <summary>PaymentMethod enum: 1 = CliQ, 2 = Cash.</summary>
    public int Method { get; set; }

    /// <summary>When the money was received (UTC).</summary>
    public DateTime PaidDate { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    /// <summary>Human reference printed on the receipt, e.g. RCP-2026-000042.</summary>
    public string ReferenceNo { get; set; } = null!;

    public string? Note { get; set; }
    /// <summary>Uploaded proof (CliQ screenshot) — file name in CableAttachments.</summary>
    public string? ReceiptImageFileName { get; set; }
    /// <summary>System-generated branded PDF — file name in CableReceipts.</summary>
    public string? GeneratedReceiptFileName { get; set; }

    public bool IsVoid { get; set; }
    public string? VoidReason { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedByUserId { get; set; }

    public virtual Subscription Subscription { get; set; } = null!;
    public virtual Payer Payer { get; set; } = null!;
}
