using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

public class OcppAuthorizedTagConfiguration : IEntityTypeConfiguration<OcppAuthorizedTag>
{
    public void Configure(EntityTypeBuilder<OcppAuthorizedTag> builder)
    {
        builder.ToTable("OcppAuthorizedTag");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.IdTag).IsRequired().HasMaxLength(50);
        builder.Property(e => e.Label).HasMaxLength(100);
        builder.Property(e => e.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.ExpiresAt).HasColumnType("datetime2(0)");
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.ChargingPoint)
            .WithMany()
            .HasForeignKey(e => e.ChargingPointId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_OcppAuthorizedTag_ChargingPoint");

        // The Authorize lookup, and "same tag once per station".
        builder.HasIndex(e => new { e.ChargingPointId, e.IdTag })
            .IsUnique()
            .HasDatabaseName("UX_OcppAuthorizedTag_Station_Tag")
            .HasFilter("[IsDeleted] = 0");
    }
}
