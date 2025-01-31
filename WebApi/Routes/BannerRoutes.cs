using Application.Banners.Queries.GetAllBanners;
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
                await mediator.Send(new GetAllBannersRequest(), cancellationToken)
            ))
            .Produces<List<GetAllBannersDto>>()
            .RequireAuthorization()
            .ProducesForbidden()
            .ProducesUnAuthorized()
            .ProducesInternalServerError()
            .WithName("Get All Banners")
            .WithSummary("Get all banners")
            .WithOpenApi();

        return app;
    }
}