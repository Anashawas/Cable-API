using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Workers;

public class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    public void Configure(EntityTypeBuilder<TrustedDevice> builder)
    {
        builder.ToTable("TrustedDevice");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.App).IsRequired().HasMaxLength(20);
        builder.Property(e => e.DeviceId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_TrustedDevice_UserAccount");

        builder.HasIndex(e => e.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_TrustedDevice_TokenHash");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_TrustedDevice_User")
            .HasFilter("[IsDeleted] = 0");
    }
}
