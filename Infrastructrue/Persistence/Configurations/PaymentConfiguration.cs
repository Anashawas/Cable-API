using Domain.Enitites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructrue.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payment");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Amount).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(e => e.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("JOD");
        builder.Property(e => e.Method).IsRequired();
        builder.Property(e => e.PaidDate).HasColumnType("datetime").IsRequired();
        builder.Property(e => e.PeriodStart).HasColumnType("datetime").IsRequired();
        builder.Property(e => e.PeriodEnd).HasColumnType("datetime").IsRequired();
        builder.Property(e => e.ReferenceNo).IsRequired().HasMaxLength(40);
        builder.Property(e => e.Note).HasMaxLength(1000);
        builder.Property(e => e.ReceiptImageFileName).HasMaxLength(260);
        builder.Property(e => e.GeneratedReceiptFileName).HasMaxLength(260);
        builder.Property(e => e.IsVoid).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.VoidReason).HasMaxLength(500);
        builder.Property(e => e.VoidedAt).HasColumnType("datetime");
        builder.Property(e => e.CreatedAt).HasColumnType("datetime");
        builder.Property(e => e.ModifiedAt).HasColumnType("datetime");

        builder.HasOne(d => d.Subscription)
            .WithMany(s => s.Payments)
            .HasForeignKey(d => d.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Payment_Subscription");

        builder.HasOne(d => d.Payer)
            .WithMany(p => p.Payments)
            .HasForeignKey(d => d.PayerId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_Payment_Payer");

        builder.HasIndex(e => e.ReferenceNo).IsUnique().HasDatabaseName("UX_Payment_ReferenceNo");
        builder.HasIndex(e => new { e.SubscriptionId, e.PaidDate }).HasDatabaseName("IX_Payment_Subscription_PaidDate");
        // Monthly collection totals group by paid date.
        builder.HasIndex(e => e.PaidDate).HasDatabaseName("IX_Payment_PaidDate");
    }
}
