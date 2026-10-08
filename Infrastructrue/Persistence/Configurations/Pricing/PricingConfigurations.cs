using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Pricing;

/// <summary>Maps Scripts/PriceAlerts_Phase1.sql.</summary>
public class TouTariffConfiguration : IEntityTypeConfiguration<TouTariff>
{
    public void Configure(EntityTypeBuilder<TouTariff> builder)
    {
        builder.ToTable("TouTariff");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EffectiveFrom).HasColumnType("datetime2(0)");
        builder.Property(e => e.Timezone).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3);
        builder.Property(e => e.Unit).IsRequired().HasMaxLength(20);
        builder.Property(e => e.Note).HasMaxLength(300);
        builder.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");
        builder.HasMany(e => e.Windows).WithOne(w => w.Tariff).HasForeignKey(w => w.TouTariffId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TouTariffWindowConfiguration : IEntityTypeConfiguration<TouTariffWindow>
{
    public void Configure(EntityTypeBuilder<TouTariffWindow> builder)
    {
        builder.ToTable("TouTariffWindow");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Key).IsRequired().HasMaxLength(30);
        builder.Property(e => e.NameEn).IsRequired().HasMaxLength(60);
        builder.Property(e => e.NameAr).IsRequired().HasMaxLength(60);
        builder.Property(e => e.Tier).IsRequired().HasMaxLength(20);
        builder.HasIndex(e => new { e.TouTariffId, e.Key }).IsUnique().HasDatabaseName("UX_TouTariffWindow_Tariff_Key");
    }
}

public class UserPriceAlertConfiguration : IEntityTypeConfiguration<UserPriceAlert>
{
    public void Configure(EntityTypeBuilder<UserPriceAlert> builder)
    {
        builder.ToTable("UserPriceAlert");
        builder.HasKey(e => e.UserAccountId);
        builder.Property(e => e.Windows).IsRequired().HasMaxLength(200);
        builder.Property(e => e.UpdatedAt).HasColumnType("datetime2(0)");
        builder.HasOne(e => e.UserAccount).WithOne().HasForeignKey<UserPriceAlert>(e => e.UserAccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PriceAlertLogConfiguration : IEntityTypeConfiguration<PriceAlertLog>
{
    public void Configure(EntityTypeBuilder<PriceAlertLog> builder)
    {
        builder.ToTable("PriceAlertLog");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.WindowKey).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Language).HasMaxLength(5);
        builder.Property(e => e.SentAt).HasColumnType("datetime2(0)");
        builder.HasIndex(e => new { e.UserAccountId, e.WindowKey, e.AlertDate }).IsUnique().HasDatabaseName("UX_PriceAlertLog_User_Window_Date");
    }
}
