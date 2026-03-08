namespace Application.Common.Models.Emails;

public class EmailResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public int AttemptCount { get; set; }
    public DateTime SentAt { get; set; }
}
