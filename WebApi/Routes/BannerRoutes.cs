using Application.Banners.Commands.AddBanner;
using Application.Banners.Commands.DeleteBanner;
using Application.Banners.Commands.UpdateBanner;
using Application.Banners.Queries.GetAllBanners;
using Cable.Requests.Banners;
using MediatR;

namespace Cable.Routes;

public static class BannerRoutes
{
    public static IEndpointRouteBuilder MapBannerRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/banners")
            .WithTags("Banners")
            .MapGroup();


        return app;
    }


    private static RouteGroupBuilder MapGroup(this RouteGroupBuilder app)
    {
        app.MapGet("/GetAllBanners", async (IMediator mediator, CancellationToken cancellationToken) => Results.Ok(
                await mediator.Send(new GetAllBannersRequest(), cancellationToken)))
            .Produces<List<GetAllBannersDto>>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get All Banners")
            .WithSummary("Get all banners")
            .WithOpenApi();

        app.MapPost("/AddBanner",
                async (IMediator mediator, AddBannerCommand request, CancellationToken cancellationToken) => Results.Ok(
                    await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Add Banner")
            .WithSummary("Add a new banner")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the banner";
                return op;
            });
        app.MapPut("/UpdateBanner/{id:int}",
                async (IMediator mediator, UpdateBannerRequest request, CancellationToken cancellationToken, int id) =>
                    await mediator.Send(
                        new UpdateBannerCommand(id, request.Name, request.Phone, request.Email, request.StartDate,
                            request.EndDate), cancellationToken))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Update Banner")
            .WithSummary("Update a banner")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the banner";
                return op;
            });
        
        app.MapDelete("/DeleteBanner/{id:int}",
                async (IMediator mediator, int id, CancellationToken cancellationToken) =>
                    await mediator.Send(new DeleteBannerCommand(id), cancellationToken))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Delete Banner")
            .WithSummary("Delete a banner")
            .WithOpenApi(op =>
            {
                op.Parameters[0].Required = true;
                op.Parameters[0].Description = "The id of the banner";
                return op;
            });

        return app;
    }
}