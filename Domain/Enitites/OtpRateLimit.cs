namespace Domain.Enitites;

public partial class OtpRateLimit
{
    public int Id { get; set; }

    public string PhoneNumber { get; set; } = null!;

    public int RequestCount { get; set; }

    public DateTime WindowStart { get; set; }

    public DateTime LastRequestAt { get; set; }

    public bool IsBlocked { get; set; }

    public DateTime? BlockedUntil { get; set; }
}