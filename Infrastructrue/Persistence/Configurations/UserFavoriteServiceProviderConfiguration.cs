using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class UserFavoriteServiceProviderConfiguration : IEntityTypeConfiguration<UserFavoriteServiceProvider>
{
    public void Configure(EntityTypeBuilder<UserFavoriteServiceProvider> builder)
    {
        builder.ToTable("UserFavoriteServiceProvider");

        builder.HasKey(e => e.Id);

        // Unique constraint with soft delete filter
        builder.HasIndex(e => new { e.UserId, e.ServiceProviderId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_UserFavoriteServiceProvider_Unique");

        // Foreign Keys
        builder.HasOne(x => x.User)
            .WithMany(x => x.FavoriteServiceProviders)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserFavoriteServiceProvider_UserAccount");

        builder.HasOne(x => x.ServiceProvider)
            .WithMany(x => x.UserFavorites)
            .HasForeignKey(x => x.ServiceProviderId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_UserFavoriteServiceProvider_ServiceProvider");

        // Audit field configurations
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");
    }
}
