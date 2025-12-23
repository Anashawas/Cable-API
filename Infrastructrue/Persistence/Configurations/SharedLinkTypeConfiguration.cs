using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Enitites;

namespace Infrastructrue.Persistence.Configurations;

public partial class SharedLinkTypeConfiguration : IEntityTypeConfiguration<SharedLinkType>
{
    public void Configure(EntityTypeBuilder<SharedLinkType> entity)
    {
        entity.ToTable("SharedLinkType");

        entity.HasIndex(e => e.TypeName)
            .IsUnique();

        entity.Property(e => e.TypeName)
            .HasMaxLength(50)
            .IsRequired();

        entity.Property(e => e.Description)
            .HasMaxLength(255);

        entity.Property(e => e.BaseUrl)
            .HasMaxLength(255)
            .IsRequired();

        entity.Property(e => e.IsActive)
            .HasDefaultValue(true);

        OnConfigurePartial(entity);
    }

    partial void OnConfigurePartial(EntityTypeBuilder<SharedLinkType> entity);
}