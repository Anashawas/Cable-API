using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class LoyaltyPointTransactionConfiguration : IEntityTypeConfiguration<LoyaltyPointTransaction>
{
    public void Configure(EntityTypeBuilder<LoyaltyPointTransaction> builder)
    {
        builder.ToTable("LoyaltyPointTransaction");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TransactionType)
            .IsRequired();

        builder.Property(e => e.Points)
            .IsRequired();

        builder.Property(e => e.BalanceAfter)
            .IsRequired();

        builder.Property(e => e.ReferenceType)
            .HasMaxLength(100);

        builder.Property(e => e.Note)
            .HasMaxLength(500);

        builder.Property(e => e.ExpiresAt)
            .HasColumnType("datetime");

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.Account)
            .WithMany(p => p.Transactions)
            .HasForeignKey(d => d.UserLoyaltyAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_LoyaltyPointTransaction_UserLoyaltyAccount");

        builder.HasOne(d => d.Action)
            .WithMany(p => p.Transactions)
            .HasForeignKey(d => d.LoyaltyPointActionId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_LoyaltyPointTransaction_LoyaltyPointAction");

        builder.HasOne(d => d.Season)
            .WithMany(p => p.Transactions)
            .HasForeignKey(d => d.LoyaltySeasonId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_LoyaltyPointTransaction_LoyaltySeason");

        // Indexes
        builder.HasIndex(e => e.UserLoyaltyAccountId)
            .HasDatabaseName("IX_LoyaltyPointTransaction_UserLoyaltyAccountId");

        builder.HasIndex(e => e.LoyaltySeasonId)
            .HasDatabaseName("IX_LoyaltyPointTransaction_LoyaltySeasonId");

        builder.HasIndex(e => e.CreatedAt)
            .HasDatabaseName("IX_LoyaltyPointTransaction_CreatedAt");

        builder.HasIndex(e => e.TransactionType)
            .HasDatabaseName("IX_LoyaltyPointTransaction_TransactionType");

        builder.HasIndex(e => new { e.ReferenceType, e.ReferenceId })
            .HasDatabaseName("IX_LoyaltyPointTransaction_ReferenceType_ReferenceId");
    }
}
