using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserFavoriteChargingPointConfiguration : IEntityTypeConfiguration<UserFavoriteChargingPoint>
{
    public void Configure(EntityTypeBuilder<UserFavoriteChargingPoint> builder)
    {
        builder.ToTable("UserFavoriteChargingPoint");

        builder.HasKey(e => e.Id);

        // Unique constraint with soft delete filter
        builder.HasIndex(e => new { e.UserId, e.ChargingPointId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_UserFavoriteChargingPoint_Unique");

        // Foreign key relationships
        builder.HasOne(x => x.User)
            .WithMany(x => x.FavoriteChargingPoints)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserFavoriteChargingPoint_UserAccount");

        builder.HasOne(x => x.ChargingPoint)
            .WithMany(x => x.UserFavorites)
            .HasForeignKey(x => x.ChargingPointId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserFavoriteChargingPoint_ChargingPoint");

        // Audit field configurations
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");
    }
}
