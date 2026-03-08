using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ChargingPointUpdateRequestConfiguration : IEntityTypeConfiguration<ChargingPointUpdateRequest>
{
    public void Configure(EntityTypeBuilder<ChargingPointUpdateRequest> builder)
    {
        builder.ToTable("ChargingPointUpdateRequest");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RequestStatus).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255);
        builder.Property(x => x.CountryName).HasMaxLength(100);
        builder.Property(x => x.CityName).HasMaxLength(100);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.MethodPayment).HasMaxLength(100);
        builder.Property(x => x.FromTime).HasMaxLength(10);
        builder.Property(x => x.ToTime).HasMaxLength(10);
        builder.Property(x => x.OwnerPhone).HasMaxLength(50);
        builder.Property(x => x.Service).HasMaxLength(255);
        builder.Property(x => x.OfferDescription).HasMaxLength(1000);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.NewIcon).HasMaxLength(255);
        builder.Property(x => x.OldIcon).HasMaxLength(255);
        builder.Property(x => x.RejectionReason).HasMaxLength(500);

        builder.HasOne(x => x.ChargingPoint)
            .WithMany()
            .HasForeignKey(x => x.ChargingPointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RequestedBy)
            .WithMany()
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReviewedBy)
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ChargingPointId)
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(x => x.RequestStatus)
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(x => x.RequestedByUserId)
            .HasFilter("[IsDeleted] = 0");
    }
}
