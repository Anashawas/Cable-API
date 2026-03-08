using Application.ConversionRates.Commands.CreateConversionRate;
using Application.ConversionRates.Commands.UpdateConversionRate;
using Application.ConversionRates.Queries.GetAllConversionRates;
using Cable.Requests.ConversionRates;
using Cable.WebApi.OpenAPI;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class ConversionRateRoutes
{
    public static IEndpointRouteBuilder MapConversionRateRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/conversion-rates")
            .WithTags("Conversion Rates")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Get all conversion rates
        app.MapGet("/GetAllConversionRates",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllConversionRatesRequest(), cancellationToken)))
            .Produces<List<ConversionRateDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get All Conversion Rates")
            .WithSummary("Get all points conversion rates")
            .WithOpenApi();

        // Create conversion rate
        app.MapPost("/CreateConversionRate",
                async (IMediator mediator, CreateConversionRateRequest request,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new CreateConversionRateCommand(
                        request.Name, request.CurrencyCode, request.PointsPerUnit,
                        request.IsDefault, request.IsActive
                    ), cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Create Conversion Rate")
            .WithSummary("Create a new points conversion rate (admin)")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                return op;
            });

        // Update conversion rate
        app.MapPut("/UpdateConversionRate/{id:int}",
                async (IMediator mediator, [FromRoute] int id, UpdateConversionRateRequest request,
                    CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new UpdateConversionRateCommand(
                        id, request.Name, request.CurrencyCode, request.PointsPerUnit,
                        request.IsDefault, request.IsActive
                    ), cancellationToken);
                    return Results.Ok();
                })
            .Produces(200)
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Update Conversion Rate")
            .WithSummary("Update an existing conversion rate (admin)")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The ID of the conversion rate to update";
                op.RequestBody.Required = true;
                return op;
            });

        return app;
    }
}
