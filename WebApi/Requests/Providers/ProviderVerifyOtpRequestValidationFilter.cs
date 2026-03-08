using Application.Common.Extensions;
using Cable.Core;
using FluentValidation;

namespace Cable.Requests.Providers;

public class ProviderVerifyOtpRequestValidationFilter : IEndpointFilter
{
    private readonly IValidator<ProviderVerifyOtpRequest> _validator;

    public ProviderVerifyOtpRequestValidationFilter(IValidator<ProviderVerifyOtpRequest> validator) => _validator = validator;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.GetArgument<ProviderVerifyOtpRequest>(0);

        if (request == null)
        {
            throw new DataValidationException("request", "Invalid request details");
        }

        var validationResult = _validator.Validate(request);

        if (!validationResult.IsValid)
        {
            throw new DataValidationException(validationResult.Errors.ToErrorsDictionary());
        }

        return await next(context);
    }
}
