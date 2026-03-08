namespace Application.Common.Models.Reports;

public record UtilityInvoiceRequest(
    string? Email,
    string? Phone,
    string? CustomerName,
    string? ServiceName,
    double? TotalAmount,
    DateTime? PaymentDate,
    DateOnly? DurationFrom,
    DateOnly? DurationTo,
    string?InvoiceName,
    string?TitleName,
    string?PrivacyName
    )
{
    public int Year =>   DateTime.Now.Year;   
}
