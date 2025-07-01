using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class CarConfiguration :  IEntityTypeConfiguration<Car>
{
    public void Configure(EntityTypeBuilder<Car> builder)
    {
        builder.ToTable("Car");
        builder.HasOne(x=>x.CarModel).WithMany(x=>x.Cars).HasForeignKey(x=>x.CarModelId).HasConstraintName("FK_CarModel_Car");
    }
}