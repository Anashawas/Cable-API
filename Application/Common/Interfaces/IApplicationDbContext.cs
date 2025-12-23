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
    Task<int> SaveChanges(CancellationToken cancellationToken = default);
}