using Cable.Core.Localization;

namespace Cable.Core;

public class DataValidationException() : Exception(Resources.ValidationFailure)
{
    public IDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]>();


    public DataValidationException(Dictionary<string, string[]> errors) : this()
    {
        foreach(var error in errors)
        {
            Errors.Add(error.Key, error.Value);
        }
    }
    
    public DataValidationException(String key, string message) : this()
    {
        Errors[key] = [message];
    }

    
}