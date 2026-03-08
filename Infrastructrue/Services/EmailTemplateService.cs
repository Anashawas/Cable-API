using System.Text.RegularExpressions;
using Application.Common.Interfaces;
using Infrastructrue.Options;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Services;

public class EmailTemplateService : IEmailTemplateService
{
    private readonly EmailOptions _emailOptions;
    private readonly string _templateBasePath;

    public EmailTemplateService(IOptions<EmailOptions> emailOptions)
    {
        _emailOptions = emailOptions.Value;
        _templateBasePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            _emailOptions.TemplateBasePath);
    }

    public async Task<string> RenderTemplateAsync(
        string templateName,
        Dictionary<string, object> variables,
        string language = "en")
    {
        // Load template file
        var templatePath = Path.Combine(_templateBasePath, language, $"{templateName}.html");

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Email template not found: {templatePath}");
        }

        var template = await File.ReadAllTextAsync(templatePath);

        // Add global variables (website url, support email, etc.)
        variables.TryAdd("websiteUrl", _emailOptions.WebsiteUrl);
        variables.TryAdd("supportEmail", _emailOptions.SupportEmail);
        variables.TryAdd("logoUrl", _emailOptions.LogoUrl);

        // Simple variable replacement ({{variableName}})
        foreach (var variable in variables)
        {
            var placeholder = $"{{{{{variable.Key}}}}}";
            template = template.Replace(placeholder, variable.Value?.ToString() ?? string.Empty);
        }

        return template;
    }

    public string HtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove script and style elements
        html = Regex.Replace(html, @"<(script|style)[^>]*?>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Remove HTML comments
        html = Regex.Replace(html, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

        // Convert <br> to newlines
        html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // Convert </p> and </div> to double newlines
        html = Regex.Replace(html, @"</(?:p|div)>", "\n\n", RegexOptions.IgnoreCase);

        // Convert list items to bullets
        html = Regex.Replace(html, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);

        // Remove all remaining HTML tags
        html = Regex.Replace(html, @"<[^>]*>", string.Empty);

        // Decode HTML entities
        html = System.Net.WebUtility.HtmlDecode(html);

        // Clean up whitespace
        html = Regex.Replace(html, @"[ \t]+", " ");
        html = Regex.Replace(html, @"\n\s+", "\n");
        html = Regex.Replace(html, @"\n{3,}", "\n\n");

        return html.Trim();
    }
}
