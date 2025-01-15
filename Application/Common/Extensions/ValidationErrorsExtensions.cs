namespace Application.Common.Extensions;

public static class ValidationErrorsExtensions
{
    public static Dictionary<string, string[]> ToErrorsDictionary(this List<FluentValidation.Results.ValidationFailure> failures)
        => failures.GroupBy(e => e.PropertyName, e => e.ErrorMessage)
            .ToDictionary(failureGroup => failureGroup.Key, failureGroup => failureGroup.ToArray());
}
