using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ServiceProviderRateConfiguration : IEntityTypeConfiguration<ServiceProviderRate>
{
    public void Configure(EntityTypeBuilder<ServiceProviderRate> builder)
    {
        builder.ToTable("ServiceProviderRate");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Rating)
            .IsRequired();

        builder.Property(e => e.AVGRating)
            .IsRequired();

        builder.Property(e => e.Comment)
            .HasMaxLength(1000);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.ServiceProvider)
            .WithMany(p => p.ServiceProviderRates)
            .HasForeignKey(d => d.ServiceProviderId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ServiceProviderRate_ServiceProvider");

        builder.HasOne(d => d.User)
            .WithMany(p => p.ServiceProviderRates)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ServiceProviderRate_UserAccount");

        // Indexes
        builder.HasIndex(e => e.ServiceProviderId)
            .HasDatabaseName("IX_ServiceProviderRate_ServiceProviderId");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_ServiceProviderRate_UserId");

        builder.HasIndex(e => new { e.UserId, e.ServiceProviderId })
            .HasDatabaseName("IX_ServiceProviderRate_UserId_ServiceProviderId");
    }
}
