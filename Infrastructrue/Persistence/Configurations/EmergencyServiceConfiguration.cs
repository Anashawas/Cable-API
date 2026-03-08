using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class EmergencyServiceConfiguration : IEntityTypeConfiguration<EmergencyService>
{
    public void Configure(EntityTypeBuilder<EmergencyService> builder)
    {
        builder.ToTable("EmergencyService");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.ImageUrl)
            .HasMaxLength(500);

        builder.Property(e => e.SubscriptionType)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.PriceDetails)
            .HasMaxLength(255);

        builder.Property(e => e.ActionUrl)
            .HasMaxLength(500);

        builder.Property(e => e.PhoneNumber)
            .HasMaxLength(40);

        builder.Property(e => e.WhatsAppNumber)
            .HasMaxLength(40);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        // Indexes for performance
        builder.HasIndex(e => new { e.IsActive, e.IsDeleted })
            .HasDatabaseName("IX_EmergencyService_IsActive_IsDeleted")
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(e => e.SortOrder)
            .HasDatabaseName("IX_EmergencyService_SortOrder");
    }
}
