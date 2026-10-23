using Application.Common.Interfaces;
using Application.Common.Models;
using Infrastructrue.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Web;
using Cable.Core.Exceptions;

namespace Infrastructrue.Services;

public class SmsService : ISmsService
{
    private readonly HttpClient _httpClient;
    private readonly SmsOptions _smsOptions;
    private readonly ILogger<SmsService> _logger;

    public SmsService(HttpClient httpClient, IOptions<SmsOptions> smsOptions, ILogger<SmsService> logger)
    {
        _httpClient = httpClient;
        _smsOptions = smsOptions.Value;
        _logger = logger;

        // Configure HttpClient timeout
        _httpClient.Timeout = TimeSpan.FromSeconds(_smsOptions.TimeoutSeconds);
    }

    public async Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        var result = await SendSmsWithResultAsync(phoneNumber, message, cancellationToken);
        return result.Success;
    }

    public async Task<SmsResult> SendSmsWithResultAsync(string phoneNumber, string message,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(_smsOptions.Username) || string.IsNullOrEmpty(_smsOptions.Password))
            {
                _logger.LogError("SMS service credentials not configured properly");
                throw new NotFoundException("SMS service credentials not configured");
            }
            
            var formattedPhoneNumber = Cable.Core.Utilities.PhoneNumberUtility.FormatForSms(phoneNumber);
            if (formattedPhoneNumber == null)
            {
                _logger.LogError("Invalid phone number format: {PhoneNumber}", phoneNumber);
                return new SmsResult(false, "Invalid phone number format", null, null, 0);
            }
            string? lastErrorMessage = null;
            
            for (int attempt = 1; attempt <= _smsOptions.MaxRetryAttempts; attempt++)
            {
                try
                {
                    var (success, response, providerId) =
                        await SendSmsAttemptWithDetails(formattedPhoneNumber, message, cancellationToken);
                    if (success)
                    {
                        _logger.LogInformation("SMS sent successfully to {PhoneNumber} on attempt {Attempt}",
                            formattedPhoneNumber, attempt);
                        return new SmsResult(true, "SMS sent successfully", providerId, startTime, attempt);
                    }

                    lastErrorMessage = response;

                    if (attempt < _smsOptions.MaxRetryAttempts)
                    {
                        _logger.LogWarning("SMS send attempt {Attempt} failed for {PhoneNumber}, retrying...",
                            attempt, formattedPhoneNumber);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                            cancellationToken); // Exponential backoff
                    }
                }
                catch (Exception ex) when (attempt < _smsOptions.MaxRetryAttempts)
                {
                    lastErrorMessage = ex.Message;
                    _logger.LogWarning(ex, "SMS send attempt {Attempt} failed for {PhoneNumber}, retrying...",
                        attempt, formattedPhoneNumber);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
            }

            _logger.LogError("Failed to send SMS to {PhoneNumber} after {MaxAttempts} attempts",
                formattedPhoneNumber, _smsOptions.MaxRetryAttempts);
            return new SmsResult(false, lastErrorMessage ?? "Failed after maximum retry attempts", null, null,
                _smsOptions.MaxRetryAttempts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SMS to {PhoneNumber}", phoneNumber);
            return new SmsResult(false, ex.Message, null, null, 0);
        }
    }

    private async Task<(bool success, string response, string? providerId)> SendSmsAttemptWithDetails(
        string phoneNumber, string message, CancellationToken cancellationToken)
    {
        // Build SMS April API URL with query parameters
        var queryParams = new StringBuilder(_smsOptions.ApiUrl);

        // Add required parameters for SMS April API
        var parameters = new Dictionary<string, string>
        {
            { "comm", "sendsms" },
            { "user", _smsOptions.Username },
            { "pass", _smsOptions.Password },
            { "to", phoneNumber },
            { "message", message },
            { "sender", _smsOptions.SenderId },
            { "date", DateTime.UtcNow.ToString("M/d/yyyy") },
            { "time", DateTime.UtcNow.ToString("H:mm") }
        };

        // Build query string
        if (!_smsOptions.ApiUrl.Contains("?"))
        {
            queryParams.Append("?");
        }
        else
        {
            queryParams.Append("&");
        }

        var queryString = string.Join("&", parameters.Select(kvp =>
            $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));

        queryParams.Append(queryString);
        var requestUrl = queryParams.ToString();

        _logger.LogDebug("Sending SMS request to: {Url}",
            requestUrl.Replace(_smsOptions.Password, "***"));

        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        // Log response for debugging
        _logger.LogDebug("SMS API Response: Status={StatusCode}, Content={Content}",
            response.StatusCode, responseContent);

        // Extract provider ID from response if available
        string? providerId = ExtractProviderIdFromResponse(responseContent);

        // SMS April API typically returns success indicators in the response content
        // You may need to adjust this based on actual API response format
        // SMS April answers HTTP 200 even when it refuses the message: the refusal is a negative
        // code in the body (e.g. -113 not enough balance), which contains no "error" / "fail".
        var providerError = DescribeSendError(responseContent);
        var isSuccess = response.IsSuccessStatusCode &&
                        providerError is null &&
                        !responseContent.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                        !responseContent.Contains("fail", StringComparison.OrdinalIgnoreCase);

        if (!isSuccess)
        {
            _logger.LogWarning("SMS API returned error. Status: {StatusCode}, Response: {Response}, Meaning: {Meaning}",
                response.StatusCode, responseContent, providerError ?? "unknown");
        }

        return (isSuccess, providerError ?? responseContent, providerId);
    }

    /// <summary>
    /// SMS April send answers: "result:smsId:mobile" per number (result 1 = sent, u = unknown status,
    /// -2 = invalid destination, -999 = provider failed) or a bare error code (-100 … -116).
    /// Returns null when the answer is not a known refusal.
    /// </summary>
    private static string? DescribeSendError(string responseContent)
    {
        var firstLine = responseContent.Trim().Split('\n', 2)[0].Trim();
        var code = firstLine.Split(':', 2)[0].Trim();
        if (!code.StartsWith('-'))
            return null;

        return code switch
        {
            "-2" => "Invalid destination or country not supported (-2)",
            "-999" => "Message not sent by the SMS provider (-999)",
            "-100" => "Missing parameters (-100)",
            "-110" => "Wrong SMS username or password (-110)",
            "-111" => "SMS account not activated (-111)",
            "-112" => "SMS account blocked (-112)",
            "-113" => "Not enough SMS balance (-113)",
            "-114" => "SMS service not available right now (-114)",
            "-115" => "Sender name not available (-115)",
            "-116" => "Invalid sender name (-116)",
            _ => $"SMS provider error ({code})"
        };
    }

    public async Task<SmsAccountStatus> CheckAccountAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_smsOptions.Username) || string.IsNullOrEmpty(_smsOptions.Password))
            return new SmsAccountStatus(false, "SMS credentials not configured", false, "Unknown", null,
                _smsOptions.MinBalance);

        var account = await QueryProviderAsync("chk_user", null, cancellationToken);
        var balance = await QueryProviderAsync("chk_balance", null, cancellationToken);
        var sender = await QueryProviderAsync("senderstatus",
            new Dictionary<string, string> { { "sendertext", _smsOptions.SenderId } }, cancellationToken);

        var accountText = account switch
        {
            "999" => "Active",
            "-110" => "Wrong username or password",
            "-111" => "Account not activated",
            "-112" => "Account blocked",
            null => "Provider not reachable",
            _ => $"Unexpected answer: {account[..Math.Min(account.Length, 40)]}"
        };
        var senderText = sender switch
        {
            "999" => "Active",
            "-110" => "Wrong username or password",
            "-116" => $"Sender name '{_smsOptions.SenderId}' not registered",
            "-117" => $"Sender name '{_smsOptions.SenderId}' inactive",
            "-118" => $"Sender name '{_smsOptions.SenderId}' rejected",
            null => "Provider not reachable",
            _ => $"Unexpected answer: {sender[..Math.Min(sender.Length, 40)]}"
        };
        decimal? balanceValue = decimal.TryParse(balance, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                                && parsed >= 0
            ? parsed
            : null;

        return new SmsAccountStatus(account == "999", accountText, sender == "999", senderText, balanceValue,
            _smsOptions.MinBalance);
    }

    /// <summary>One read-only provider command (no SMS is sent). Null when the provider cannot be reached.</summary>
    private async Task<string?> QueryProviderAsync(string command, IDictionary<string, string>? extra,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            { "comm", command },
            { "user", _smsOptions.Username },
            { "pass", _smsOptions.Password }
        };
        foreach (var (key, value) in extra ?? new Dictionary<string, string>())
            parameters[key] = value;

        var separator = _smsOptions.ApiUrl.Contains('?') ? "&" : "?";
        var requestUrl = _smsOptions.ApiUrl + separator + string.Join("&", parameters.Select(kvp =>
            $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));

        try
        {
            using var response = await _httpClient.GetAsync(requestUrl, cancellationToken);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (response.IsSuccessStatusCode)
                return body;

            _logger.LogWarning("SMS provider check {Command} answered HTTP {StatusCode}", command, response.StatusCode);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "SMS provider check {Command} failed", command);
            return null;
        }
    }

    private static string? ExtractProviderIdFromResponse(string responseContent)
    {
        try
        {
            // SMS April may return a message ID or reference in the response
            // This is a placeholder implementation - adjust based on actual API response format
            if (string.IsNullOrEmpty(responseContent))
                return null;

            // Look for common patterns in SMS provider responses
            if (responseContent.Contains("id:", StringComparison.OrdinalIgnoreCase))
            {
                var startIndex = responseContent.IndexOf("id:", StringComparison.OrdinalIgnoreCase) + 3;
                var endIndex = responseContent.IndexOfAny([' ', '\n', '\r', ',', '}'], startIndex);
                if (endIndex == -1) endIndex = responseContent.Length;
                return responseContent.Substring(startIndex, endIndex - startIndex).Trim();
            }

            // If response contains only digits (likely a message ID)
            if (responseContent.All(char.IsDigit) && responseContent.Length > 3)
            {
                return responseContent;
            }

            // Fallback: use timestamp-based ID
            return $"SMS-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
        }
        catch
        {
            return null;
        }
    }

    // Removed FormatPhoneNumber method - now using centralized Cable.Core.Utilities.PhoneNumberUtility.FormatForSms
}