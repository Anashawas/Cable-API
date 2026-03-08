namespace Application.Common.Models;

/// <summary>
/// Provider authentication session data stored in cache
/// </summary>
public class ProviderAuthSession
{
    public int UserId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
