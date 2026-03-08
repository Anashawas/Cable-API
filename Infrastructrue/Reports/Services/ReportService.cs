using Application.Common.Interfaces;
using Application.Common.Models.Reports;
using Cable.Core.Exceptions;
using FastReport;
using FastReport.Export.PdfSimple;
using Infrastructrue.Common.Helpers;
using Infrastructrue.Options;
using Infrastructrue.Reports.Enums;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using Cable.Core;

namespace Infrastructrue.Reports.Services;

public class ReportService(
    IApplicationDbContext applicationDbContext,
    IOptions<ReportsOptions> reportOptions
) : IReportService
{
    private readonly ReportsOptions _reportOptions = reportOptions.Value;
    private string BaseUrl => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _reportOptions.BaseUrl);


    public async Task<byte[]> GenerateUtilityCableInvoice(UtilityInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var reportPath = ReportHelper.SetUpReportPath(BaseUrl,
            nameof(ReportNames.UtilityCableInvoiceReport));

        if (!File.Exists(reportPath))
            throw new NotFoundException($"Report template not found: {reportPath}");

        using var report = new Report();
        report.Load(reportPath);

        report.RegisterData(new[] { request }, "UtilityInvoice");

        report.GetDataSource("UtilityInvoice").Enabled = true;

        cancellationToken.ThrowIfCancellationRequested();
        await report.PrepareAsync(cancellationToken);

        using var pdfExport = new PDFSimpleExport();
        pdfExport.ShowProgress = false;

        await using var stream = new MemoryStream();
        report.Export(pdfExport, stream);

        return stream.ToArray();
    }
}