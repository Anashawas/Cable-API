using System.Reflection;
using Application.Common.Interfaces;
using Domain.Enitites;
using Infrastructrue.Common.Extensions;
using Infrastructrue.Options;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Infrastructrue.Persistence;

public partial class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IEnumerable<SaveChangesInterceptor> saveChangesInterceptor,
    IOptions<DatabaseOptions> databaseSettingsOptions,
    IMediator mediator)
    : DbContext(options), IApplicationDbContext
{
    #region Memebers

    private readonly IEnumerable<SaveChangesInterceptor> _saveChangesInterceptors = saveChangesInterceptor;

    private readonly DatabaseOptions _databaseSettings = databaseSettingsOptions.Value;
    private readonly IMediator _mediator = mediator;

    #endregion

    #region Properties

    public  DbSet<SystemVersion> SystemVersions { get; set; }
    public DbSet<UserCar> UserCars { get; set; }
    public DbSet<CarType> CarTypes { get; set; }
    public DbSet<CarModel> CarModels { get; set; }
    public DbSet<Banner> Banners { get; set; }
    public DbSet<BannerDuration> BannerDurations { get; set; }
    public DbSet<BannerAttachment> BannerAttachments { get; set; }
    public DbSet<ChargingPointAttachment> ChargingPointAttachments { get; set; }
    public DbSet<ChargingPlug> ChargingPlugs { get; set; }
    public DbSet<ChargingPoint> ChargingPoints { get; set; }
    public DbSet<ChargingPointType> ChargingPointTypes { get; set; }
    public DbSet<PlugType> PlugTypes { get; set; }
    public DbSet<Privilege> Privilages { get; set; }
    public DbSet<Rate> Rates { get; set; }
    public DbSet<UserRate> UserRates { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<RolePrivlage> RolePrivlages { get; set; }
    public DbSet<Status> Statuses { get; set; }
    public DbSet<StationType> StationTypes { get; set; }
    public DbSet<UserAccount> UserAccounts { get; set; }
    public DbSet<UserComplaint> UserComplaints { get; set; }
    public DbSet<NotificationToken> NotificationTokens { get; set; }
    public DbSet<SharedLink> SharedLinks { get; set; }
    public DbSet<SharedLinkType> SharedLinkTypes { get; set; }
    public DbSet<SharedLinkUsage> SharedLinkUsages { get; set; }
    public DbSet<OtpRateLimit> OtpRateLimits { get; set; }
    public DbSet<PhoneVerification> PhoneVerifications { get; set; }
    public DbSet<UserFavoriteChargingPoint> UserFavoriteChargingPoints { get; set; }
    public DbSet<NotificationType> NotificationTypes { get; set; }
    public DbSet<Domain.Enitites.NotificationInbox> NotificationInboxes { get; set; }
    public DbSet<PasswordReset> PasswordResets { get; set; }
    public DbSet<ChargingPointUpdateRequest> ChargingPointUpdateRequests { get; set; }
    public DbSet<ChargingPointUpdateRequestAttachment> ChargingPointUpdateRequestAttachments { get; set; }
    public DbSet<EmergencyService> EmergencyServices { get; set; }
    public DbSet<EmergencyServiceAttachment> EmergencyServiceAttachments { get; set; }

    // Service Provider DbSets
    public DbSet<ServiceCategory> ServiceCategories { get; set; }
    public DbSet<ServiceProvider> ServiceProviders { get; set; }
    public DbSet<ServiceProviderAttachment> ServiceProviderAttachments { get; set; }
    public DbSet<ServiceProviderRate> ServiceProviderRates { get; set; }
    public DbSet<UserFavoriteServiceProvider> UserFavoriteServiceProviders { get; set; }

    // Social Media DbSets
    public DbSet<SocialMediaPlatform> SocialMediaPlatforms { get; set; }
    public DbSet<SocialLink>          SocialLinks          { get; set; }

    // Provider managers (workers)
    public DbSet<ProviderManager> ProviderManagers { get; set; }

    // Offers & Transactions DbSets
    public DbSet<PointsConversionRate> PointsConversionRates { get; set; }
    public DbSet<ProviderOffer> ProviderOffers { get; set; }
    public DbSet<OfferAttachment> OfferAttachments { get; set; }
    public DbSet<OfferTransaction> OfferTransactions { get; set; }
    public DbSet<ProviderSettlement> ProviderSettlements { get; set; }
    public DbSet<ProviderPayment> ProviderPayments { get; set; }
    public DbSet<ProviderWalletTransaction> ProviderWalletTransactions { get; set; }

    // Partner Transactions DbSets
    public DbSet<PartnerAgreement> PartnerAgreements { get; set; }
    public DbSet<PartnerTransaction> PartnerTransactions { get; set; }

    // Loyalty System DbSets
    public DbSet<LoyaltyPointAction> LoyaltyPointActions { get; set; }
    public DbSet<LoyaltyTier> LoyaltyTiers { get; set; }
    public DbSet<LoyaltySeason> LoyaltySeasons { get; set; }
    public DbSet<LoyaltyBoost> LoyaltyBoosts { get; set; }
    public DbSet<LoyaltyBoostProvider> LoyaltyBoostProviders { get; set; }
    public DbSet<UserSeasonProgress> UserSeasonProgresses { get; set; }
    public DbSet<UserLoyaltyAccount> UserLoyaltyAccounts { get; set; }
    public DbSet<LoyaltyPointTransaction> LoyaltyPointTransactions { get; set; }
    public DbSet<LoyaltyReward> LoyaltyRewards { get; set; }
    public DbSet<UserRewardRedemption> UserRewardRedemptions { get; set; }

    // Analytics engine DbSets
    public DbSet<AnalyticsEvent> AnalyticsEvents { get; set; }
    public DbSet<AnalyticsDailyRollup> AnalyticsDailyRollups { get; set; }

    // Premium station subscriptions
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<Payer> Payers { get; set; }
    public DbSet<Payment> Payments { get; set; }

    // Lookups
    public DbSet<ChargerBrand> ChargerBrands { get; set; }
    public DbSet<ChargingPointChargerBrand> ChargingPointChargerBrands { get; set; }
    public DbSet<CarModelSize> CarModelSizes { get; set; }

    // Terms & conditions
    public DbSet<TermsVersion> TermsVersions { get; set; }
    public DbSet<UserTermsAcceptance> UserTermsAcceptances { get; set; }

    // Provider announcements to favorites
    public DbSet<ProviderFavoriteNotification> ProviderFavoriteNotifications { get; set; }

    // Notification body suggestions (admin-managed, Part B F5)
    public DbSet<NotificationTemplate> NotificationTemplates { get; set; }

    // Home-screen ads (advertisers, campaigns, settings, welcome messages)
    public DbSet<Advertiser> Advertisers { get; set; }
    public DbSet<Campaign> Campaigns { get; set; }
    public DbSet<AppSetting> AppSettings { get; set; }
    public DbSet<Announcement> Announcements { get; set; }
    public DbSet<AnnouncementUserState> AnnouncementUserStates { get; set; }

    // Cable Connect (OCPP 1.6J) — written by Cable.Ocpp, read by the API
    public DbSet<OcppChargePoint> OcppChargePoints { get; set; }
    public DbSet<OcppConnector> OcppConnectors { get; set; }
    public DbSet<OcppTransaction> OcppTransactions { get; set; }
    public DbSet<OcppMeterValue> OcppMeterValues { get; set; }
    public DbSet<OcppAuthorizedTag> OcppAuthorizedTags { get; set; }
    public DbSet<OcppRawMessage> OcppRawMessages { get; set; }
    public DbSet<OcppCommand> OcppCommands { get; set; }
    public DbSet<OcppAlert> OcppAlerts { get; set; }
    public DbSet<OcppUserIdTag> OcppUserIdTags { get; set; }

    // Time-of-use tariff + price alerts
    public DbSet<TouTariff> TouTariffs { get; set; }
    public DbSet<TouTariffWindow> TouTariffWindows { get; set; }
    public DbSet<UserPriceAlert> UserPriceAlerts { get; set; }
    public DbSet<PriceAlertLog> PriceAlertLogs { get; set; }

    #endregion

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_saveChangesInterceptors);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(_databaseSettings.DefaultSchema);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public async Task<int> SaveChanges(CancellationToken cancellationToken = default)
    {
        await _mediator.DispatchDomainEvents(this);
        return await SaveChangesAsync(cancellationToken);
    }
}