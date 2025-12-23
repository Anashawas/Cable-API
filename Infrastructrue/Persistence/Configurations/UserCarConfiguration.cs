using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserCarConfiguration : IEntityTypeConfiguration<UserCar>
{
    public void Configure(EntityTypeBuilder<UserCar> builder)
    {
        builder.ToTable("UserCar");
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.UserId, e.CarModelId, e.PlugTypeId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_UserCar_Unique");
        builder.HasOne(x=>x.UserAccount).WithMany(x=>x.UserCars).HasForeignKey(x=>x.UserId).HasConstraintName("FK_UserCar_UserAccount");
        builder.HasOne(x=>x.CarModel).WithMany(x=>x.UserCars).HasForeignKey(x=>x.CarModelId).HasConstraintName("FK_UserCar_CarModel");
        builder.HasOne(x=>x.PlugType).WithMany(x=>x.UserCars).HasForeignKey(x=>x.PlugTypeId).HasConstraintName("FK_UserCar_PlugType");
    }
}