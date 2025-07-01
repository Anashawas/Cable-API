using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class CarTypeConfiguration :  IEntityTypeConfiguration<CarType>
{
    public void Configure(EntityTypeBuilder<CarType> builder)
    {
        builder.ToTable("CarType");
        builder.Property(x=>x.Name).HasMaxLength(50);
    }
}