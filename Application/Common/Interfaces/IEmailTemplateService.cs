namespace Application.Common.Interfaces;

/// <summary>
/// Service for rendering email templates
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Render HTML template with variables
    /// </summary>
    Task<string> RenderTemplateAsync(
        string templateName,
        Dictionary<string, object> variables,
        string language = "en");

    /// <summary>
    /// Get plain text version of email (strip HTML)
    /// </summary>
    string HtmlToPlainText(string html);
}
