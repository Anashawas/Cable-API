using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class BannerDurationConfiguration : IEntityTypeConfiguration<BannerDuration>
{
    public void Configure(EntityTypeBuilder<BannerDuration> builder)
    {
        builder.ToTable(nameof(BannerDuration));

        builder.HasOne(x => x.Banner).WithMany(x => x.BannerDurations).HasForeignKey(x => x.BannerId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_BannerDuration_Banner");
            

    }
}