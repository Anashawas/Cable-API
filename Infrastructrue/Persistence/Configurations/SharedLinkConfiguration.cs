using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Enitites;

namespace Infrastructrue.Persistence.Configurations;

public partial class SharedLinkConfiguration : IEntityTypeConfiguration<SharedLink>
{
    public void Configure(EntityTypeBuilder<SharedLink> entity)
    {
        entity.ToTable("SharedLink");

        entity.HasIndex(e => e.LinkToken)
            .IsUnique();

        entity.Property(e => e.CreatedAt)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("getdate()");

        entity.Property(e => e.ModifiedAt)
            .HasColumnType("datetime2")
            .HasDefaultValueSql("getdate()");

        entity.Property(e => e.LinkToken)
            .HasMaxLength(255)
            .IsRequired();

        entity.Property(e => e.LinkType)
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(e => e.Parameters)
            .HasColumnType("nvarchar(max)");

        entity.Property(e => e.ExpiresAt)
            .HasColumnType("datetime2");

        entity.Property(e => e.MaxUsage)
            .HasDefaultValue(1);

        entity.Property(e => e.CurrentUsage)
            .HasDefaultValue(0);

        entity.Property(e => e.IsActive)
            .HasDefaultValue(true);

        entity.Property(e => e.IsDeleted)
            .HasDefaultValue(false);

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<SharedLink> entity);
}