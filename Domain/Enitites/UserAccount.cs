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

    // Single-device session enforcement
    public string? SecurityStamp { get; set; }
    
    public virtual ICollection<ChargingPoint> ChargingPoints { get; set; } = new List<ChargingPoint>();

    public virtual ICollection<Rate> Rates { get; set; } = new List<Rate>();

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<PhoneVerification> PhoneVerifications { get; set; } = new List<PhoneVerification>();

    public virtual ICollection<UserComplaint> UserComplaints { get; set; } = new List<UserComplaint>();
    public ICollection<UserCar>  UserCars { get; set; } =  new List<UserCar>();
    public ICollection<NotificationToken>  NotificationTokens { get; set; } =  new List<NotificationToken>();
    public ICollection<UserFavoriteChargingPoint> FavoriteChargingPoints { get; set; } = new List<UserFavoriteChargingPoint>();
    public virtual ICollection<NotificationInbox> NotificationInboxes { get; set; } = new List<NotificationInbox>();

    // Service Provider navigation properties
    public virtual ICollection<ServiceProvider> OwnedServiceProviders { get; set; } = new List<ServiceProvider>();
    public virtual ICollection<ServiceProviderRate> ServiceProviderRates { get; set; } = new List<ServiceProviderRate>();
    public virtual ICollection<UserFavoriteServiceProvider> FavoriteServiceProviders { get; set; } = new List<UserFavoriteServiceProvider>();

    // Offers & Transactions navigation properties
    public virtual ICollection<ProviderOffer> ProposedOffers { get; set; } = new List<ProviderOffer>();
    public virtual ICollection<OfferTransaction> OfferTransactions { get; set; } = new List<OfferTransaction>();

    // Partner Transactions navigation properties
    public virtual ICollection<PartnerTransaction> PartnerTransactions { get; set; } = new List<PartnerTransaction>();

    // Loyalty navigation properties
    public virtual UserLoyaltyAccount? LoyaltyAccount { get; set; }
    public virtual ICollection<UserSeasonProgress> SeasonProgresses { get; set; } = new List<UserSeasonProgress>();
    public virtual ICollection<UserRewardRedemption> RewardRedemptions { get; set; } = new List<UserRewardRedemption>();
}