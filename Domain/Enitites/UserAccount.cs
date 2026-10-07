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

    // Single-device session enforcement, tracked per client app so the Cable
    // consumer app, the Provider/Worker mobile app and the partner web portal
    // do not evict each other. SecurityStamp = consumer app;
    // ProviderSecurityStamp = provider mobile; ProviderWebSecurityStamp = partner web.
    public string? SecurityStamp { get; set; }
    public string? ProviderSecurityStamp { get; set; }
    public string? ProviderWebSecurityStamp { get; set; }

    /// <summary>
    /// Last successful sign-in (UTC). Written on every login path.
    ///
    /// This is NOT a usage metric. Access tokens are long-lived, so somebody who
    /// opens the app daily may not have re-authenticated in months — use
    /// <see cref="LastSeenAt"/> for "is this user active".
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Last authenticated request (UTC) — the activity signal behind DAU/WAU/MAU.
    ///
    /// Written by middleware and deliberately throttled, so a burst of requests
    /// costs one write rather than hundreds. That makes it accurate to within the
    /// throttle window, which is far finer than any reporting period.
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>
    /// Last sign-in through a PARTNER client (provider mobile app or partner web
    /// portal), UTC. <see cref="LastLoginAt"/> is written by every app including
    /// the consumer one, and most station owners are also drivers — so it cannot
    /// answer "does this owner use the partner app". This can.
    /// </summary>
    public DateTime? PartnerLastLoginAt { get; set; }

    /// <summary>
    /// Last authenticated request from a PARTNER client, UTC. Same throttling as
    /// <see cref="LastSeenAt"/>; the client is read from the token's "app" claim.
    /// </summary>
    public DateTime? PartnerLastSeenAt { get; set; }

    // Update notes
    public bool HasReadUpdateNotes { get; set; }

    // Terms & conditions (denormalized current state; history in UserTermsAcceptance)
    public int? AcceptedTermsVersionId { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    
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