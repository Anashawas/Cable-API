using System.Net;
using System.Text.Json;
using Cable.Core;
using Cable.Core.Exceptions;
using Cable.WebApi.Localization;
using Cable.WebApi.Response;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cable.WebApi.Middlewares;

internal sealed class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly IDictionary<Type, Func<HttpContext, Exception, Task>> _exceptionHandlers;

    public ExceptionHandlerMiddleware(RequestDelegate next,
        ILogger<ExceptionHandlerMiddleware> logger,
        IHostEnvironment hostEnvironment,
        IOptions<JsonSerializerOptions> jsonSerializerOptions)
    {
        _next = next;
        _logger = logger;
        _hostEnvironment = hostEnvironment;
        _jsonSerializerOptions = jsonSerializerOptions.Value;

        _exceptionHandlers = new Dictionary<Type, Func<HttpContext, Exception, Task>>
        {
            { typeof(DataValidationException), HandleValidationException },
            { typeof(NotFoundException), HandleNotFoundException },
            { typeof(NotAuthorizedAccessException), HandleUnauthorizedAccessException },
            { typeof(ForbiddenAccessException), HandleForbiddenAccessException }
        };
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleException(httpContext, ex);
        }
    }


    private async Task HandleValidationException(HttpContext httpContext, Exception ex)
    {
        var exception = ex as DataValidationException;

        var problemDetails = new CableProblemDetails(exception.Errors)
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Status = StatusCodes.Status400BadRequest,
            Title = Resources.ValidationFailure,
            Detail = Resources.ValidationFailure,
            Instance = httpContext?.Request?.Path
        };

        if (problemDetails.Errors?.Count > 0)
        {
            problemDetails.Detail = problemDetails.Errors[0].Reasons.FirstOrDefault();
        }

        await WriteExceptionDetails(problemDetails, httpContext, (int)HttpStatusCode.BadRequest);
    }

    private async Task HandleNotFoundException(HttpContext httpContext, Exception ex)
    {
        var exception = ex as NotFoundException;

        var problemDetails = new CableProblemDetails()
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            Status = StatusCodes.Status404NotFound,
            Title = Resources.NotFound,
            Detail = exception.Message ?? Resources.NotFound,
            Instance = httpContext?.Request?.Path
        };

        await WriteExceptionDetails(problemDetails, httpContext, (int)HttpStatusCode.NotFound);
    }


    private async Task HandleUnauthorizedAccessException(HttpContext httpContext, Exception ex)
    {
        var details = new CableProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            Status = StatusCodes.Status401Unauthorized,
            Title = Resources.Unauthorized,
            Detail = ex.Message ?? Resources.Unauthorized,
            Instance = httpContext?.Request?.Path
        };

        await WriteExceptionDetails(details, httpContext, (int)HttpStatusCode.Unauthorized);
    }

    private async Task HandleForbiddenAccessException(HttpContext httpContext, Exception ex)
    {
        var problemDetails = new CableProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = Resources.Forbidden,
            Detail = Resources.ForbiddenDetails,
            Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.5.3",
            Instance = httpContext?.Request?.Path
        };

        await WriteExceptionDetails(problemDetails, httpContext, (int)problemDetails.Status);
    }


    private async Task HandleException(HttpContext httpContext, Exception ex)
    {
        Type type = ex.GetType();
        if (_exceptionHandlers.ContainsKey(type))
        {
            await _exceptionHandlers[type].Invoke(httpContext, ex);
            return;
        }

        await HandleUnknownException(httpContext, ex);
    }

    private async Task HandleUnknownException(HttpContext httpContext, Exception ex)
    {
        _logger.LogError(ex, "");

        var details = new CableProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = Resources.InternalError,
            Detail = !_hostEnvironment.IsProduction() || ex is CableApplicationException
                ? ex.Message
                : Resources.InternalError,
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            Instance = httpContext?.Request?.Path
        };

        await WriteExceptionDetails(details, httpContext, (int)HttpStatusCode.InternalServerError);
    }

    async Task WriteExceptionDetails(Object objectResult, HttpContext httpContext, int statusCode)
    {
        httpContext.Response.ContentType = "application/json";
        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(objectResult, _jsonSerializerOptions));
    }
}


public static class ExceptionHandlerMiddlewareExtension
{
    public static IApplicationBuilder UseCableExceptionHandlerMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionHandlerMiddleware>();
    }
}