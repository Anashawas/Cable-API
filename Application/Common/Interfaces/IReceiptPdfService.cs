namespace Application.Common.Interfaces;

/// <summary>Everything printed on a payment receipt. Dates are already in Jordan local time.</summary>
public record ReceiptData(
    string ReferenceNo,
    DateTime IssuedAtLocal,
    string EntityLabelEn,
    string EntityLabelAr,
    string EntityName,
    decimal Amount,
    string Currency,
    string MethodEn,
    string MethodAr,
    DateTime PaidDateLocal,
    DateTime PeriodStartLocal,
    DateTime PeriodEndLocal,
    int? PlanMonths,
    string PayerName,
    string? PayerPhone,
    string? Note);

public interface IReceiptPdfService
{
    /// <summary>Renders the branded bilingual receipt. Pure: no I/O, no storage.</summary>
    byte[] Generate(ReceiptData data);
}
