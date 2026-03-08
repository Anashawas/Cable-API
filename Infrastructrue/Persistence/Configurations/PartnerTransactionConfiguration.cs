using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class PartnerTransactionConfiguration : IEntityTypeConfiguration<PartnerTransaction>
{
    public void Configure(EntityTypeBuilder<PartnerTransaction> builder)
    {
        builder.ToTable("PartnerTransaction");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.TransactionCode)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ProviderId)
            .IsRequired();

        builder.Property(e => e.TransactionAmount)
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.CurrencyCode)
            .HasMaxLength(10);

        builder.Property(e => e.CommissionPercentage)
            .IsRequired();

        builder.Property(e => e.CommissionAmount)
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.PointsRewardPercentage)
            .IsRequired();

        builder.Property(e => e.PointsConversionRate)
            .IsRequired();

        builder.Property(e => e.PointsEligibleAmount)
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.CodeExpiresAt)
            .IsRequired()
            .HasColumnType("datetime");

        builder.Property(e => e.CompletedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.Agreement)
            .WithMany(p => p.Transactions)
            .HasForeignKey(d => d.PartnerAgreementId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_PartnerTransaction_PartnerAgreement");

        builder.HasOne(d => d.User)
            .WithMany(p => p.PartnerTransactions)
            .HasForeignKey(d => d.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_PartnerTransaction_User");

        builder.HasOne(d => d.ConfirmedByUser)
            .WithMany()
            .HasForeignKey(d => d.ConfirmedByUserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_PartnerTransaction_ConfirmedByUser");

        // Indexes
        builder.HasIndex(e => e.TransactionCode)
            .IsUnique()
            .HasDatabaseName("IX_PartnerTransaction_TransactionCode_Unique");

        builder.HasIndex(e => e.PartnerAgreementId)
            .HasDatabaseName("IX_PartnerTransaction_PartnerAgreementId");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_PartnerTransaction_UserId");

        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_PartnerTransaction_Status");

        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_PartnerTransaction_ProviderType_ProviderId");

        builder.HasIndex(e => e.CodeExpiresAt)
            .HasDatabaseName("IX_PartnerTransaction_CodeExpiresAt");

        builder.HasIndex(e => e.CompletedAt)
            .HasDatabaseName("IX_PartnerTransaction_CompletedAt");
    }
}
