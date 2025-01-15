using Application.Common.Extensions;
using Cable.Core;
using FluentValidation;

namespace Cable.Requests.Identity;

public class LoginRequestValidationFilter : IEndpointFilter
{
    private readonly IValidator<LoginRequest> _validator;

    public LoginRequestValidationFilter(IValidator<LoginRequest> validator) => _validator = validator;

    public async ValueTask<object> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var loginRequst = context.GetArgument<LoginRequest>(0);

        if (loginRequst == null)
        {
            throw new DataValidationException("userName", Application.Common.Localization.Resources.InvalidRequestDetails);
        }

        var validationResult = _validator.Validate(loginRequst);

        if (!validationResult.IsValid)
        {
            
            throw new DataValidationException(validationResult.Errors.ToErrorsDictionary());
        }

        return await next(context);

    }
}
