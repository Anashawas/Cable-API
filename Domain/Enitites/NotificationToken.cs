using Cable.Core.Enums;

namespace Domain.Enitites;

public partial class NotificationToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = null!;
    public string OsName { get; set; } = null!;
    public string OsVersion { get; set; } = null!;
    public string AppVersion { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public FirebaseAppType AppType { get; set; } = FirebaseAppType.UserApp;
    public virtual UserAccount UserAccount { get; set; } = null!;
}