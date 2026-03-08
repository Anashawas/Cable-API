using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserLoyaltyAccountConfiguration : IEntityTypeConfiguration<UserLoyaltyAccount>
{
    public void Configure(EntityTypeBuilder<UserLoyaltyAccount> builder)
    {
        builder.ToTable("UserLoyaltyAccount");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TotalPointsEarned)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.TotalPointsRedeemed)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.CurrentBalance)
            .IsRequired()
            .HasDefaultValue(0);

        // Blocking fields
        builder.Property(e => e.IsBlocked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.BlockedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.BlockedUntil)
            .HasColumnType("datetime");

        builder.Property(e => e.BlockReason)
            .HasMaxLength(500);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.User)
            .WithOne(p => p.LoyaltyAccount)
            .HasForeignKey<UserLoyaltyAccount>(d => d.UserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserLoyaltyAccount_UserAccount");

        builder.HasOne(d => d.BlockedByUser)
            .WithMany()
            .HasForeignKey(d => d.BlockedByUserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserLoyaltyAccount_BlockedByUser");

        // Indexes
        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasDatabaseName("IX_UserLoyaltyAccount_UserId_Unique");

        builder.HasIndex(e => e.CurrentBalance)
            .HasDatabaseName("IX_UserLoyaltyAccount_CurrentBalance");
    }
}
