using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class CarModelSizeConfiguration : IEntityTypeConfiguration<CarModelSize>
{
    public void Configure(EntityTypeBuilder<CarModelSize> builder)
    {
        builder.ToTable("CarModelSize");
        builder.Property(x => x.Name).IsRequired().HasMaxLength(50);

        builder.HasMany(x => x.CarModels)
            .WithOne(x => x.Size)
            .HasForeignKey(x => x.SizeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_CarModel_CarModelSize");
    }
}
