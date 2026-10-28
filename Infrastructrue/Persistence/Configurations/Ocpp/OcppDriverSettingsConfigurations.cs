using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations.Ocpp;

/// <summary>Maps Scripts/Sprint5_Leftovers.sql (dbo.UserOcppSetting).</summary>
public class UserOcppSettingConfiguration : IEntityTypeConfiguration<UserOcppSetting>
{
    public void Configure(EntityTypeBuilder<UserOcppSetting> builder)
    {
        builder.ToTable("UserOcppSetting");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EmailReceipts).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasDatabaseName("UX_UserOcppSetting_UserId");
    }
}

/// <summary>Maps Scripts/Sprint5_Leftovers.sql (dbo.OcppReceiptEmail).</summary>
public class OcppReceiptEmailConfiguration : IEntityTypeConfiguration<OcppReceiptEmail>
{
    public void Configure(EntityTypeBuilder<OcppReceiptEmail> builder)
    {
        builder.ToTable("OcppReceiptEmail");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Status).IsRequired().HasMaxLength(16);
        builder.Property(e => e.Error).HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnType("datetime2(0)");

        // One receipt email per session, ever.
        builder.HasIndex(e => e.OcppTransactionId)
            .IsUnique()
            .HasDatabaseName("UX_OcppReceiptEmail_OcppTransactionId");
    }
}
