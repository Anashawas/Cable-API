using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

/// <summary>Maps Scripts/Sprint5_LiveCharging_B.sql (dbo.OcppConnectorCode).</summary>
public class OcppConnectorCodeConfiguration : IEntityTypeConfiguration<OcppConnectorCode>
{
    public void Configure(EntityTypeBuilder<OcppConnectorCode> builder)
    {
        builder.ToTable("OcppConnectorCode");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Code).IsRequired().HasMaxLength(20);
        builder.Property(e => e.RevokedAt).HasColumnType("datetime2(0)");
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.Connector)
            .WithMany()
            .HasForeignKey(e => e.OcppConnectorId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_OcppConnectorCode_OcppConnector");

        // A code is never reissued, revoked or not.
        builder.HasIndex(e => e.Code)
            .IsUnique()
            .HasDatabaseName("UX_OcppConnectorCode_Code");

        // One active code per plug.
        builder.HasIndex(e => e.OcppConnectorId)
            .IsUnique()
            .HasDatabaseName("UX_OcppConnectorCode_Connector_Active")
            .HasFilter("[RevokedAt] IS NULL AND [IsDeleted] = 0");
    }
}
