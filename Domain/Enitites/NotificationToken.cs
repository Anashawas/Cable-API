namespace Domain.Enitites;

public partial class NotificationToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public Guid DeviceId { get; set; } 
    public string Token { get; set; } = null!;
    public DateTime? DeletedAt { get; set; }
    public string OsName { get; set; } = null!;
    public string OsVersion { get; set; } = null!;
    public string AppVersion { get; set; } = null!;
    public DateTime? LastSeenDateTime { get; set; }
    public UserAccount UserAccount { get; set; } = null!;
}