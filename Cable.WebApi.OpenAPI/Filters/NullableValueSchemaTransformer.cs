using System;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Cable.WebApi.OpenAPI.Filters;

public class NullableValueSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type.IsGenericType && context.JsonTypeInfo.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            schema.Example ??= new OpenApiString((string)null);
        }

        return Task.CompletedTask;
    }
}