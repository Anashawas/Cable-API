using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcement");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TitleEn).IsRequired().HasMaxLength(200);
        builder.Property(x => x.TitleAr).IsRequired().HasMaxLength(200);
        builder.Property(x => x.BodyEn).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.BodyAr).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.ImageUrl).HasMaxLength(1000);
        builder.Property(x => x.ActionUrl).HasMaxLength(1000);
        builder.Property(x => x.ActionLabelEn).HasMaxLength(100);
        builder.Property(x => x.ActionLabelAr).HasMaxLength(100);
        builder.Property(x => x.TargetType).IsRequired().HasMaxLength(20).HasDefaultValue("national");
        builder.Property(x => x.TargetCity).HasMaxLength(100);
        builder.Property(x => x.Audience).IsRequired().HasMaxLength(20).HasDefaultValue("all");

        builder.HasOne(x => x.Campaign)
            .WithMany()
            .HasForeignKey(x => x.CampaignId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Announcement_Campaign");

        builder.HasOne(x => x.Advertiser)
            .WithMany()
            .HasForeignKey(x => x.AdvertiserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Announcement_Advertiser");
    }
}

public class AnnouncementUserStateConfiguration : IEntityTypeConfiguration<AnnouncementUserState>
{
    public void Configure(EntityTypeBuilder<AnnouncementUserState> builder)
    {
        builder.ToTable("AnnouncementUserState");
        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Announcement)
            .WithMany()
            .HasForeignKey(x => x.AnnouncementId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_AnnouncementUserState_Announcement");

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_AnnouncementUserState_UserAccount");

        builder.HasIndex(x => new { x.AnnouncementId, x.UserId })
            .IsUnique()
            .HasDatabaseName("UX_AnnouncementUserState_Announcement_User");
    }
}
