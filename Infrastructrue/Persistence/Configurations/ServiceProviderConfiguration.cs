using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ServiceProviderConfiguration : IEntityTypeConfiguration<ServiceProvider>
{
    public void Configure(EntityTypeBuilder<ServiceProvider> builder)
    {
        builder.ToTable("ServiceProvider");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.Phone)
            .HasMaxLength(40);

        builder.Property(e => e.OwnerPhone)
            .HasMaxLength(40);

        builder.Property(e => e.Address)
            .HasMaxLength(1000);

        builder.Property(e => e.CountryName)
            .HasMaxLength(200);

        builder.Property(e => e.CityName)
            .HasMaxLength(200);

        builder.Property(e => e.PriceDescription)
            .HasMaxLength(500);

        builder.Property(e => e.FromTime)
            .HasMaxLength(16);

        builder.Property(e => e.ToTime)
            .HasMaxLength(16);

        builder.Property(e => e.MethodPayment)
            .HasMaxLength(200);

        builder.Property(e => e.VisitorsCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.IsVerified)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.HasOffer)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.OfferDescription)
            .HasMaxLength(2000);

        builder.Property(e => e.Note)
            .HasMaxLength(1000);

        builder.Property(e => e.WhatsAppNumber)
            .HasMaxLength(80);

        builder.Property(e => e.WebsiteUrl)
            .HasMaxLength(500);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.ModifiedAt)
            .HasColumnType("datetime");

        // Foreign Keys
        builder.HasOne(d => d.Owner)
            .WithMany(p => p.OwnedServiceProviders)
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ServiceProvider_UserAccount");

        builder.HasOne(d => d.ServiceCategory)
            .WithMany(p => p.ServiceProviders)
            .HasForeignKey(d => d.ServiceCategoryId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ServiceProvider_ServiceCategory");

        builder.HasOne(d => d.Status)
            .WithMany(p => p.ServiceProviders)
            .HasForeignKey(d => d.StatusId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ServiceProvider_Status");

        // Indexes
        builder.HasIndex(e => e.ServiceCategoryId)
            .HasDatabaseName("IX_ServiceProvider_ServiceCategoryId");

        builder.HasIndex(e => e.OwnerId)
            .HasDatabaseName("IX_ServiceProvider_OwnerId");

        builder.HasIndex(e => e.StatusId)
            .HasDatabaseName("IX_ServiceProvider_StatusId");

        builder.HasIndex(e => new { e.IsDeleted, e.IsVerified })
            .HasDatabaseName("IX_ServiceProvider_IsDeleted_IsVerified")
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(e => new { e.Latitude, e.Longitude })
            .HasDatabaseName("IX_ServiceProvider_Lat_Lng");

        // Loyalty Blocking
        builder.Property(e => e.IsLoyaltyBlocked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.LoyaltyBlockedAt)
            .HasColumnType("datetime");

        builder.Property(e => e.LoyaltyBlockedUntil)
            .HasColumnType("datetime");

        builder.Property(e => e.LoyaltyBlockReason)
            .HasMaxLength(500);

        builder.HasOne(d => d.LoyaltyBlockedByUser)
            .WithMany()
            .HasForeignKey(d => d.LoyaltyBlockedByUserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_ServiceProvider_LoyaltyBlockedByUser");

        // Loyalty Credit Limit
        builder.Property(e => e.LoyaltyCreditLimit)
            .HasColumnType("decimal(18,3)");

        builder.Property(e => e.LoyaltyCurrentBalance)
            .IsRequired()
            .HasDefaultValue(0m)
            .HasColumnType("decimal(18,3)");
    }
}
