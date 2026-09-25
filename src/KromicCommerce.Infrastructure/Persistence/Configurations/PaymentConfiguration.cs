using KromicCommerce.Domain.Orders;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Provider).IsRequired().HasMaxLength(50);
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(100);
        builder.Property(p => p.ProviderOrderId).HasMaxLength(100);
        builder.Property(p => p.Amount).IsRequired().HasColumnType("numeric(12,2)");
        builder.Property(p => p.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(p => p.Status).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(p => p.FailureReason).HasMaxLength(500);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => p.OrderId).IsUnique().HasDatabaseName("ix_payments_order");
        builder.HasIndex(p => p.ProviderPaymentId)
            .HasFilter("\"ProviderPaymentId\" IS NOT NULL")
            .HasDatabaseName("ix_payments_provider_payment");
        builder.HasIndex(p => p.ProviderOrderId)
            .HasFilter("\"ProviderOrderId\" IS NOT NULL")
            .HasDatabaseName("ix_payments_provider_order");

        builder.HasOne(p => p.Order)
            .WithMany()
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
