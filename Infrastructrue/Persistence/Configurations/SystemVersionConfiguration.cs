using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public partial class SystemVersionConfiguration :IEntityTypeConfiguration<SystemVersion>
{
    public void Configure(EntityTypeBuilder<SystemVersion> builder)
    {
        builder.ToTable("SystemVersion");
        builder.Property(x => x.Platform).HasMaxLength(50);
        builder.Property(x => x.Version).HasMaxLength(50);
        builder.Property(x => x.ForceUpdate).HasMaxLength(50);
    }
}