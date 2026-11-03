using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ProviderManagerConfiguration : IEntityTypeConfiguration<ProviderManager>
{
    public void Configure(EntityTypeBuilder<ProviderManager> builder)
    {
        builder.ToTable("ProviderManager", t =>
        {
            t.HasCheckConstraint(
                "CK_ProviderManager_ProviderType",
                "ProviderType IN (N'ServiceProvider', N'ChargingPoint')");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.ProviderType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.Privileges).HasMaxLength(500);
        builder.Property(e => e.KeepOnFree).IsRequired().HasDefaultValue(false);

        builder.Property(e => e.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ProviderManager_User");

        // The workers of a provider (several since the worker tracker; the plan sets the limit).
        builder.HasIndex(e => new { e.ProviderType, e.ProviderId })
            .HasDatabaseName("IX_ProviderManager_Provider")
            .HasFilter("[IsDeleted] = 0");

        // Fast "providers this user works for".
        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("IX_ProviderManager_User")
            .IncludeProperties(e => new { e.ProviderType, e.ProviderId, e.IsActive })
            .HasFilter("[IsDeleted] = 0");
    }
}
