using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class AdvertiserConfiguration : IEntityTypeConfiguration<Advertiser>
{
    public void Configure(EntityTypeBuilder<Advertiser> builder)
    {
        builder.ToTable("Advertiser");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(255);
        builder.Property(x => x.Contact).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(2000);
    }
}

public class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaign");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).IsRequired().HasMaxLength(20);
        builder.Property(x => x.CityArea).HasMaxLength(200);
        builder.Property(x => x.Price).HasColumnType("decimal(18,2)");

        builder.HasOne(x => x.Advertiser)
            .WithMany(x => x.Campaigns)
            .HasForeignKey(x => x.AdvertiserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Campaign_Advertiser");
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSetting");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Value).IsRequired().HasMaxLength(500);
        builder.HasIndex(x => x.Key).IsUnique().HasDatabaseName("UX_AppSetting_Key");
    }
}
