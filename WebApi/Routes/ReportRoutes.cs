using Application.Common.Interfaces;
using Application.Common.Models.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ReportRoutes
{
    public static IEndpointRouteBuilder MapReportRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/reports")
            .WithTags("Reports")
            .MapReportGenerationRoutes();

        return app;
    }

    private static RouteGroupBuilder MapReportGenerationRoutes(this RouteGroupBuilder app)
    {
        // Generate Utility Cable Invoice Report
        app.MapPost("/utility-invoice", async (
                IReportService reportService,
                [FromBody] UtilityInvoiceRequest request,
                CancellationToken cancellationToken) =>
            {
                var pdfBytes = await reportService.GenerateUtilityCableInvoice(
                    request,
                    cancellationToken);

                var fileName = $"UtilityInvoice_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

                return Results.File(
                    pdfBytes,
                    contentType: "application/pdf",
                    fileDownloadName: fileName);
            })
            .RequireAuthorization()
            .Produces<FileContentResult>(200, "application/pdf")
            .ProducesValidationProblem()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Generate Utility Invoice")
            .WithSummary("Generates a PDF invoice for utility cable services")
            .WithDescription("Creates and returns a PDF invoice based on the provided utility invoice details including customer information, payment details, and duration.")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.RequestBody.Description = "Utility invoice details including customer name, email, phone, payment amount, and service duration";
                op.Responses["200"].Description = "PDF invoice generated successfully and returned as a downloadable file";
                return op;
            });

        return app;
    }
}
