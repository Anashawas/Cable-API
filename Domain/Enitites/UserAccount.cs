using Domain.Common;

namespace Domain.Enitites;

public partial class UserAccount :BaseAuditableEntity
{
    public string Name { get; set; } = null!;

    public string Phone { get; set; } = null!;
    
    public int RoleId { get; set; }

    public string? Password { get; set; }

    public string? UserName { get; set; }

    public bool IsActive { get; set; }
    public string? Email { get; set; }

    public virtual ICollection<ChargingPoint> ChargingPoints { get; set; } = new List<ChargingPoint>();

    public virtual ICollection<Rate> Rates { get; set; } = new List<Rate>();

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<UserComplaint> UserComplaints { get; set; } = new List<UserComplaint>();
}