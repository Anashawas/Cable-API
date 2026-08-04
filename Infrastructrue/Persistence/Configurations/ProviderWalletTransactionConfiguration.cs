using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ProviderWalletTransactionConfiguration : IEntityTypeConfiguration<ProviderWalletTransaction>
{
    public void Configure(EntityTypeBuilder<ProviderWalletTransaction> builder)
    {
        builder.ToTable("ProviderWalletTransaction");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ProviderId)
            .IsRequired();

        builder.Property(e => e.TransactionType)
            .IsRequired();

        builder.Property(e => e.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.BalanceAfter)
            .IsRequired()
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.ReferenceType)
            .HasMaxLength(50);

        builder.Property(e => e.Note)
            .HasMaxLength(1000);

        builder.Property(e => e.RecordedByUserId);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.RecordedByUser)
            .WithMany()
            .HasForeignKey(d => d.RecordedByUserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ProviderWalletTransaction_RecordedByUser");

        // Indexes
        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_ProviderWalletTransaction_Provider");

        builder.HasIndex(e => e.RecordedByUserId)
            .HasDatabaseName("IX_ProviderWalletTransaction_RecordedByUser");
    }
}
