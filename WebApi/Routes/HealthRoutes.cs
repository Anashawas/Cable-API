using Application.Health.Queries;
using MediatR;

namespace Cable.Routes;

/// <summary>
/// Public health checks for uptime monitors (UptimeRobot). They answer GET and HEAD
/// (monitors send HEAD by default) with 200 when healthy and 503 when not.
/// HEAD is mapped separately and left out of the OpenAPI document: the generator
/// describes one HTTP method per endpoint and fails the build on a GET+HEAD route.
/// </summary>
public static class HealthRoutes
{
    public static IEndpointRouteBuilder MapHealthRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/health")
            .WithTags("Health")
            .MapRoutes();
        return app;
    }

    private static async Task<IResult> ApiHealth(IMediator mediator, CancellationToken cancellationToken)
    {
        var health = await mediator.Send(new GetApiHealthRequest(), cancellationToken);
        return health.Ok
            ? Results.Ok(health)
            : Results.Json(health, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> SmsHealth(IMediator mediator, CancellationToken cancellationToken)
    {
        var health = await mediator.Send(new GetSmsHealthRequest(), cancellationToken);
        return health.Ok
            ? Results.Ok(health)
            : Results.Json(health, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        app.MapGet("/", ApiHealth)
            .Produces<ApiHealthDto>()
            .Produces<ApiHealthDto>(StatusCodes.Status503ServiceUnavailable)
            .WithName("API health")
            .WithSummary("Public: 200 when the API runs and reaches its database, 503 otherwise. Also answers HEAD. For uptime monitors.")
            .WithOpenApi();
        app.MapMethods("/", [HttpMethods.Head], ApiHealth)
            .ExcludeFromDescription();

        app.MapGet("/sms", SmsHealth)
            .Produces<SmsHealthDto>()
            .Produces<SmsHealthDto>(StatusCodes.Status503ServiceUnavailable)
            .WithName("SMS health")
            .WithSummary("Public: 200 when OTP SMS can be sent (provider account and sender name active, balance above SmsService:MinBalance), 503 with 'problem' otherwise. Asks the provider at most every 5 minutes (1 minute while failing); sends no SMS. Also answers HEAD.")
            .WithOpenApi();
        app.MapMethods("/sms", [HttpMethods.Head], SmsHealth)
            .ExcludeFromDescription();

        return app;
    }
}
