using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class PartnerAgreementConfiguration : IEntityTypeConfiguration<PartnerAgreement>
{
    public void Configure(EntityTypeBuilder<PartnerAgreement> builder)
    {
        builder.ToTable("PartnerAgreement");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.ProviderId)
            .IsRequired();

        builder.Property(e => e.CommissionPercentage)
            .IsRequired();

        builder.Property(e => e.PointsRewardPercentage)
            .IsRequired();

        builder.Property(e => e.CodeExpiryMinutes)
            .IsRequired()
            .HasDefaultValue(30);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.Note)
            .HasMaxLength(500);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.ConversionRate)
            .WithMany(p => p.PartnerAgreements)
            .HasForeignKey(d => d.PointsConversionRateId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_PartnerAgreement_PointsConversionRate");

        // Indexes
        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_PartnerAgreement_ProviderType_ProviderId");

        builder.HasIndex(e => e.IsActive)
            .HasDatabaseName("IX_PartnerAgreement_IsActive");

        builder.HasIndex(e => e.IsDeleted)
            .HasDatabaseName("IX_PartnerAgreement_IsDeleted");
    }
}
