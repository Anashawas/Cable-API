using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserCarConfiguration : IEntityTypeConfiguration<UserCar>
{
    public void Configure(EntityTypeBuilder<UserCar> builder)
    {
        builder.ToTable("UserCar");
        builder.HasKey(x =>new {x.UserId, x.CarId});
        builder.HasOne(x=>x.UserAccount).WithMany(x=>x.UserCars).HasForeignKey(x=>x.UserId).HasConstraintName("FK_UserCar_UserAccount");
        builder.HasOne(x=>x.Car).WithMany(x=>x.UserCars).HasForeignKey(x=>x.CarId).HasConstraintName("FK_UserCar_Car");
        builder.HasOne(x=>x.PlugType).WithMany(x=>x.UserCars).HasForeignKey(x=>x.PlugTypeId).HasConstraintName("FK_UserCar_PlugType");
    }
}