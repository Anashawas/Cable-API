namespace Domain.Enitites;

public partial class SharedLinkUsage
{
    public int Id { get; set; }
    public int SharedLinkId { get; set; }
    public int? UserId { get; set; }
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public DateTime UsedAt { get; set; } = DateTime.Now;
    public bool IsSuccessful { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public virtual SharedLink SharedLink { get; set; } = null!;
    public virtual UserAccount? User { get; set; }
}