namespace Domain.Enitites;

public partial class PhoneVerification
{
    public int Id { get; set; }

    public string PhoneNumber { get; set; } = null!;

    public string OtpCode { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public bool IsVerified { get; set; }

    public bool IsUsed { get; set; }

    public int AttemptCount { get; set; }

    public int? UserId { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime? ModifiedAt { get; set; }

    public int? ModifiedBy { get; set; }

    public bool IsDeleted { get; set; }

    public virtual UserAccount? User { get; set; }
}