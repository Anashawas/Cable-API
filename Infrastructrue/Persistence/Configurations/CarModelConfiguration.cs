using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class CarModelConfiguration : IEntityTypeConfiguration<CarModel>
{
    public void Configure(EntityTypeBuilder<CarModel> builder)
    {
        builder.ToTable("CarModel");
        builder.Property(x => x.Name).HasMaxLength(50);
        builder.HasOne(x=>x.CarType).WithMany(x=>x.CarModels).HasForeignKey(x=>x.CarTypeId).HasConstraintName("FK_CarModel_CarType");
    }
}