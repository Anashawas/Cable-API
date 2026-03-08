using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserSeasonProgressConfiguration : IEntityTypeConfiguration<UserSeasonProgress>
{
    public void Configure(EntityTypeBuilder<UserSeasonProgress> builder)
    {
        builder.ToTable("UserSeasonProgress");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.SeasonPointsEarned)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.TierLevel)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.User)
            .WithMany(p => p.SeasonProgresses)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserSeasonProgress_UserAccount");

        builder.HasOne(d => d.Season)
            .WithMany(p => p.UserProgresses)
            .HasForeignKey(d => d.LoyaltySeasonId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserSeasonProgress_LoyaltySeason");

        builder.HasOne(d => d.Tier)
            .WithMany(p => p.UserSeasonProgresses)
            .HasForeignKey(d => d.TierLevel)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserSeasonProgress_LoyaltyTier");

        // Indexes
        builder.HasIndex(e => new { e.UserId, e.LoyaltySeasonId })
            .IsUnique()
            .HasDatabaseName("IX_UserSeasonProgress_UserId_SeasonId_Unique");

        builder.HasIndex(e => e.LoyaltySeasonId)
            .HasDatabaseName("IX_UserSeasonProgress_LoyaltySeasonId");

        builder.HasIndex(e => e.SeasonPointsEarned)
            .HasDatabaseName("IX_UserSeasonProgress_SeasonPointsEarned");
    }
}
