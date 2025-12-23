using System.Security.Principal;
using Domain.Common;

namespace Domain.Enitites;

public partial class UserAccount :BaseAuditableEntity
{
    public string? Name { get; set; } 

    public string? Phone { get; set; } 
    
    public int RoleId { get; set; }

    public string? Password { get; set; }

    public string? RegistrationProvider { get; set; }
    public string? FirebaseUId { get; set; }
    public bool IsActive { get; set; }
    public string? Email { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    
    public bool IsPhoneVerified { get; set; }
    public DateTime? PhoneVerifiedAt { get; set; }
    
    public virtual ICollection<ChargingPoint> ChargingPoints { get; set; } = new List<ChargingPoint>();

    public virtual ICollection<Rate> Rates { get; set; } = new List<Rate>();

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<PhoneVerification> PhoneVerifications { get; set; } = new List<PhoneVerification>();

    public virtual ICollection<UserComplaint> UserComplaints { get; set; } = new List<UserComplaint>();
    public ICollection<UserCar>  UserCars { get; set; } =  new List<UserCar>();
    public ICollection<NotificationToken>  NotificationTokens { get; set; } =  new List<NotificationToken>();
}