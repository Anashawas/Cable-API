using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

/// <summary>Maps Scripts/OcppConnect_Phase2.sql.</summary>
public class OcppCommandConfiguration : IEntityTypeConfiguration<OcppCommand>
{
    public void Configure(EntityTypeBuilder<OcppCommand> builder)
    {
        builder.ToTable("OcppCommand");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Action).IsRequired().HasMaxLength(64);
        builder.Property(e => e.RequestPayload).IsRequired();
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20);
        builder.Property(e => e.ResultStatus).HasMaxLength(40);
        builder.Property(e => e.ErrorCode).HasMaxLength(64);
        builder.Property(e => e.ErrorDescription).HasMaxLength(500);
        builder.Property(e => e.CompletedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.ChargePoint)
            .WithMany()
            .HasForeignKey(e => e.OcppChargePointId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_OcppCommand_OcppChargePoint");

        builder.HasIndex(e => new { e.OcppChargePointId, e.CreatedAt })
            .HasDatabaseName("IX_OcppCommand_ChargePoint_CreatedAt")
            .IsDescending(false, true);
    }
}
