using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class ChargerBrandConfiguration : IEntityTypeConfiguration<ChargerBrand>
{
    public void Configure(EntityTypeBuilder<ChargerBrand> builder)
    {
        builder.ToTable("ChargerBrand");
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);

        builder.HasIndex(x => x.Name)
            .IsUnique()
            .HasDatabaseName("UX_ChargerBrand_Name");
    }
}
