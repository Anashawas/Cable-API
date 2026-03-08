using Application.Common.Models.Reports;

namespace Application.Common.Interfaces;

public interface IReportService
{
    Task <byte[]> GenerateUtilityCableInvoice (UtilityInvoiceRequest request, CancellationToken cancellationToken = default);
}