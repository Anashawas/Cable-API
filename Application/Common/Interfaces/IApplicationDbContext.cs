using Domain.Enitites;
using Microsoft.EntityFrameworkCore;

namespace Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<NotificationToken> NotificationTokens { get; set; }
    DbSet<UserCar> UserCars { get; set; }
    DbSet<CarType> CarTypes { get; set; }
    DbSet<CarModel> CarModels { get; set; }
    DbSet<Banner> Banners { get; set; }
    DbSet<BannerDuration> BannerDurations { get; set; }
    DbSet<BannerAttachment> BannerAttachments { get; set; }

    DbSet<SystemVersion> SystemVersions { get; set; }
    DbSet<ChargingPointAttachment> ChargingPointAttachments { get; set; }

    DbSet<ChargingPlug> ChargingPlugs { get; set; }

    DbSet<ChargingPoint> ChargingPoints { get; set; }

    DbSet<ChargingPointType> ChargingPointTypes { get; set; }

    DbSet<PlugType> PlugTypes { get; set; }

    DbSet<Privilege> Privilages { get; set; }

    DbSet<Rate> Rates { get; set; }

    DbSet<Role> Roles { get; set; }

    DbSet<RolePrivlage> RolePrivlages { get; set; }

    DbSet<Status> Statuses { get; set; }

    DbSet<StationType> StationTypes { get; set; }

    DbSet<UserAccount> UserAccounts { get; set; }

    DbSet<UserComplaint> UserComplaints { get; set; }
    DbSet<SharedLink> SharedLinks { get; set; }
    DbSet<SharedLinkType> SharedLinkTypes { get; set; }

    DbSet<SharedLinkUsage> SharedLinkUsages  { get; set; }
    DbSet<OtpRateLimit> OtpRateLimits { get; set; }
    DbSet<PhoneVerification> PhoneVerifications { get; set; }
    DbSet<UserFavoriteChargingPoint> UserFavoriteChargingPoints { get; set; }
    DbSet<NotificationType> NotificationTypes { get; set; }
    DbSet<Domain.Enitites.NotificationInbox> NotificationInboxes { get; set; }
    DbSet<PasswordReset> PasswordResets { get; set; }
    DbSet<ChargingPointUpdateRequest> ChargingPointUpdateRequests { get; set; }
    DbSet<ChargingPointUpdateRequestAttachment> ChargingPointUpdateRequestAttachments { get; set; }
    DbSet<EmergencyService> EmergencyServices { get; set; }
    DbSet<EmergencyServiceAttachment> EmergencyServiceAttachments { get; set; }

    // Service Provider DbSets
    DbSet<ServiceCategory> ServiceCategories { get; set; }
    DbSet<ServiceProvider> ServiceProviders { get; set; }
    DbSet<ServiceProviderAttachment> ServiceProviderAttachments { get; set; }
    DbSet<ServiceProviderRate> ServiceProviderRates { get; set; }
    DbSet<UserFavoriteServiceProvider> UserFavoriteServiceProviders { get; set; }

    // Offers & Transactions DbSets
    DbSet<PointsConversionRate> PointsConversionRates { get; set; }
    DbSet<ProviderOffer> ProviderOffers { get; set; }
    DbSet<OfferTransaction> OfferTransactions { get; set; }
    DbSet<ProviderSettlement> ProviderSettlements { get; set; }
    DbSet<ProviderPayment> ProviderPayments { get; set; }

    // Partner Transactions DbSets
    DbSet<PartnerAgreement> PartnerAgreements { get; set; }
    DbSet<PartnerTransaction> PartnerTransactions { get; set; }

    // Loyalty System DbSets
    DbSet<LoyaltyPointAction> LoyaltyPointActions { get; set; }
    DbSet<LoyaltyTier> LoyaltyTiers { get; set; }
    DbSet<LoyaltySeason> LoyaltySeasons { get; set; }
    DbSet<UserSeasonProgress> UserSeasonProgresses { get; set; }
    DbSet<UserLoyaltyAccount> UserLoyaltyAccounts { get; set; }
    DbSet<LoyaltyPointTransaction> LoyaltyPointTransactions { get; set; }
    DbSet<LoyaltyReward> LoyaltyRewards { get; set; }
    DbSet<UserRewardRedemption> UserRewardRedemptions { get; set; }

    Task<int> SaveChanges(CancellationToken cancellationToken = default);
}