using System.Text.Json;
using System.Text.Json.Serialization;
using Cable.WebApi.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Cable.WebApi.Response;

public class CableProblemDetails : ProblemDetails
{
    [JsonPropertyName("errors")]
    public List<FieldError> Errors { get; set; } = [];

    public CableProblemDetails()
    {
        Title = Resources.ProblemResponse;
    }

    public CableProblemDetails(IDictionary<string, string[]> errorsDictionary) : this()
    {
        CreateFieldErrorsList(errorsDictionary);
    }

    public CableProblemDetails(string key, string message) : this()
    {
        Errors.Add(new FieldError()
        {
            Name = key,
            Reasons = [message]
        });
    }

    private void CreateFieldErrorsList(IDictionary<string, string[]> errorsDictionary)
    {
        if (errorsDictionary == null || errorsDictionary.Count == 0)
        {
            return;
        }

        Errors = errorsDictionary.Select(x => new FieldError()
        {
            Name = JsonNamingPolicy.CamelCase.ConvertName(x.Key),
            Reasons = x.Value
        }).ToList();
    }
}
public class FieldError
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("reasons")]
    public string[] Reasons { get; set; }
}