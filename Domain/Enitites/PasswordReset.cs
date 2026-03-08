using Domain.Common;

namespace Domain.Enitites;

public class PasswordReset : BaseEntity
{
    public int UserId { get; set; }

    public string Code { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public int FailedAttempts { get; set; }

    public string? IpAddress { get; set; }

    // Navigation property
    public virtual UserAccount User { get; set; } = null!;
}
