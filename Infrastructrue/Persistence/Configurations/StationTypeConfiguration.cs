using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public partial class StationTypeConfiguration:IEntityTypeConfiguration<StationType>
{
    public void Configure(EntityTypeBuilder<StationType> builder)
    {
        builder.ToTable("StationType");
    }
}