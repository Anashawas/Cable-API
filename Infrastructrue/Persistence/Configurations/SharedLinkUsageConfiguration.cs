using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Enitites;

namespace Infrastructrue.Persistence.Configurations;

public partial class SharedLinkUsageConfiguration : IEntityTypeConfiguration<SharedLinkUsage>
{
    public void Configure(EntityTypeBuilder<SharedLinkUsage> entity)
    {
        entity.ToTable("SharedLinkUsage");

        entity.Property(e => e.DeviceInfo)
            .HasMaxLength(500);

        entity.Property(e => e.IpAddress)
            .HasMaxLength(45);

        entity.Property(e => e.UsedAt)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("getdate()");

        entity.Property(e => e.IsSuccessful)
            .HasDefaultValue(true);

        entity.Property(e => e.ErrorMessage)
            .HasColumnType("nvarchar(max)");

        entity.HasOne(d => d.SharedLink)
            .WithMany(p => p.SharedLinkUsages)
            .HasForeignKey(d => d.SharedLinkId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("FK_SharedLinkUsage_SharedLink");

        entity.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("FK_SharedLinkUsage_UserAccount");

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<SharedLinkUsage> entity);
}