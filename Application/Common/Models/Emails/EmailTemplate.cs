namespace Application.Common.Models.Emails;

public class EmailTemplate
{
    public string TemplateName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public Dictionary<string, object> Variables { get; set; } = new();
    public string Language { get; set; } = "en";
}
