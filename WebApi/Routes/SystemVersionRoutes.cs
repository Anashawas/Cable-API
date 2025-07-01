using Application.SystemVersions.Commands.AddSystemVersion;
using Application.SystemVersions.Commands.AddSystemVersionUpdate;
using Application.SystemVersions.Queries.CheckSystemVerison;
using Application.SystemVersions.Queries.GetAllSystemVersions;
using MediatR;

namespace Cable.Routes;

public static class SystemVersionRoutes
{
    public static IEndpointRouteBuilder MapSystemVersionRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/systemversion")
            .WithTags("SystemVersion")
            .MapRoutes();
        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllSystemVersions",
            async (IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetAllSystemVersionsRequest(), cancellationToken)))
            .Produces<List<GetAllSystemVersionsDto>>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .ProducesUnAuthorized()
            .WithName("Get all System Versions")
            .WithSummary("Get all System Versions")
            .WithOpenApi();
        
        app.MapPost("/CheckSystemVersion",
                async (IMediator mediator,CheckSystemVersionRequest  request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<bool>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .ProducesUnAuthorized()
            .WithName("Check System Version")
            .WithSummary("Check system version")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the system version";
                return op;
            });
        
        app.MapPost("/AddSystemVersion",
                async (IMediator mediator,AddSystemVersionCommand  request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .ProducesNotFound()
            .ProducesUnAuthorized()
            .WithName("add system version")
            .WithSummary("add system version")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the system version";
                return op;
            });
        
        app.MapPut("/UpdateSystemVersion",
                async (IMediator mediator,UpdateSystemVersionCommand  request, CancellationToken cancellationToken) =>
                    await mediator.Send(request, cancellationToken))
            .Produces(200)
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .ProducesNotFound()
            .ProducesUnAuthorized()
            .WithName("update system version")
            .WithSummary("update system version")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the system version";
                return op;
            });


        
        
        return app;
    }
}