using Cable.WebApi.Response;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Microsoft.AspNetCore.Http;

public static class OpenApiExtensions
{
    public static RouteHandlerBuilder ProducesUnAuthorized(this RouteHandlerBuilder builder, string contentType = null)
    {
        return builder.ProducesCableValidationProblem((int)StatusCodes.Status401Unauthorized, contentType);
    }

    public static RouteHandlerBuilder ProducesForbidden(this RouteHandlerBuilder builder, string contentType = null)
    {
        return builder.ProducesCableValidationProblem((int)StatusCodes.Status403Forbidden, contentType);
    }

    public static RouteHandlerBuilder ProducesNotFound(this RouteHandlerBuilder builder, string contentType = null)
    {
        return builder.ProducesCableValidationProblem((int)StatusCodes.Status404NotFound, contentType);
    }
    

    public static RouteHandlerBuilder ProducesCableValidationProblem(this RouteHandlerBuilder builder,
        int statusCode = 400, string contentType = null)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            contentType = "application/problem+json";
        }

        return builder.Produces(statusCode, typeof(CableProblemDetails), contentType);
    }

    public static RouteHandlerBuilder ProducesInternalServerError(this RouteHandlerBuilder builder,
        string contentType = null)
    {
        return builder.ProducesCableValidationProblem((int)StatusCodes.Status500InternalServerError, contentType);
    }
}