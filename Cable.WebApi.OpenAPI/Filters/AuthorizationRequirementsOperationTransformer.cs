using Cable.Core.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Cable.WebApi.OpenAPI.Filters;

public class AuthorizationRequirementsOperationTransformer : IOpenApiOperationTransformer
{
    

    public AuthorizationRequirementsOperationTransformer()
    {
    }
    

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
     
        var requiredScopes = context.Description.ActionDescriptor.EndpointMetadata?
            .OfType<AuthorizeAttribute>()
            .Select(attr => attr.Policy)
            .Where(policy => !string.IsNullOrEmpty(policy))
            .Distinct()
            .ToList() ?? [];

        if (!requiredScopes.Any()) return Task.CompletedTask;
        if (operation.Responses.All(x => x.Key != "401"))
        {
            operation.Responses.Add("401", new OpenApiResponse { Description = "Unauthorized" });
        }
        else
        {
            operation.Responses["401"].Description = "Unauthorized";
        }

        if (operation.Responses.All(x => x.Key != "403"))
        {
            operation.Responses.Add("403", new OpenApiResponse { Description = "Forbidden" });
        }
        else
        {
            operation.Responses["401"].Description = "Forbidden";
        }

        var oAuthScheme = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Id = "Bearer",
                Type = ReferenceType.SecurityScheme
            }
        };

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new OpenApiSecurityRequirement
            {
                [oAuthScheme] = requiredScopes.ToList()
            }
        };

        return Task.CompletedTask;
    }
}