using Application.Common.Models;
using Application.Terms.Commands.AcceptTerms;
using Application.Terms.Commands.PublishTermsVersion;
using Application.Terms.Queries.GetAllTermsVersions;
using Application.Terms.Queries.GetCurrentTerms;
using Application.Terms.Queries.GetTermsVersionById;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Cable.Routes;

public static class TermsRoutes
{
    public static IEndpointRouteBuilder MapTermsRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/terms")
            .WithTags("Terms & Conditions")
            .MapRoutes();

        return app;
    }

    private static RouteGroupBuilder MapRoutes(this RouteGroupBuilder app)
    {
        // Anonymous on purpose: the registration screen shows the terms before
        // an account exists. With a token, the caller's role picks the document.
        app.MapGet("/GetCurrentTerms", async (IMediator mediator, CancellationToken cancellation) =>
                Results.Ok(await mediator.Send(new GetCurrentTermsRequest(), cancellation)))
            .Produces<GetCurrentTermsDto?>()
            .ProducesInternalServerError()
            .WithName("Get current terms")
            .WithSummary("Get the active terms & conditions for the caller (role-specific first, general fallback; anonymous allowed). Null when nothing is published")
            .WithOpenApi();

        app.MapPost("/AcceptTerms",
                async (IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new AcceptTermsCommand(), cancellationToken)))
            .Produces<AcceptTermsResult>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Accept terms")
            .WithSummary("Record the caller's acceptance of the currently active terms version for their role (idempotent)")
            .WithOpenApi();

        app.MapPost("/admin/PublishTermsVersion",
                async (IMediator mediator, PublishTermsVersionCommand request, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(request, cancellationToken)))
            .Produces<int>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesValidationProblem()
            .ProducesInternalServerError()
            .WithName("Publish terms version")
            .WithSummary("Admin: publish a new terms version (roleId null = all roles). Deactivates the previous active version in that scope, forcing re-acceptance")
            .WithOpenApi(op =>
            {
                op.RequestBody.Required = true;
                op.Responses["200"].Description = "The id of the new terms version";
                return op;
            });

        app.MapGet("/admin/GetAllTermsVersions",
                async (IMediator mediator, [FromQuery] int? page, [FromQuery] int? pageSize,
                        CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetAllTermsVersionsRequest(page, pageSize), cancellationToken)))
            .Produces<PagedResult<TermsVersionListDto>>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesInternalServerError()
            .WithName("Get all terms versions")
            .WithSummary("Admin: list all published terms versions with acceptance counts (content excluded)")
            .WithOpenApi();

        app.MapGet("/admin/GetTermsVersionById/{id:int}",
                async (IMediator mediator, [FromRoute] int id, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new GetTermsVersionByIdRequest(id), cancellationToken)))
            .Produces<TermsVersionDetailDto>()
            .RequireAuthorization()
            .ProducesUnAuthorized()
            .ProducesForbidden()
            .ProducesNotFound()
            .ProducesInternalServerError()
            .WithName("Get terms version by id")
            .WithSummary("Admin: one terms version with its full Arabic + English content")
            .WithOpenApi();

        return app;
    }
}
