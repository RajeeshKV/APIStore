using KromicCommerce.Domain.Orders;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber).IsRequired().HasMaxLength(50);
        builder.Property(o => o.Status).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(o => o.PaymentMethod).IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(o => o.CurrencyCode).IsRequired().HasMaxLength(3);

        builder.Property(o => o.Subtotal).IsRequired().HasColumnType("numeric(12,2)");
        builder.Property(o => o.ShippingAmount).IsRequired().HasColumnType("numeric(12,2)");
        builder.Property(o => o.CodFee).IsRequired().HasColumnType("numeric(12,2)").HasDefaultValue(0m);
        builder.Property(o => o.DiscountAmount).IsRequired().HasColumnType("numeric(12,2)");
        builder.Property(o => o.TaxAmount).IsRequired().HasColumnType("numeric(12,2)");
        builder.Property(o => o.GrandTotal).IsRequired().HasColumnType("numeric(12,2)");
        builder.Property(o => o.AppliedCouponCode).HasMaxLength(50);

        builder.Property(o => o.TrackingNumber).HasMaxLength(200);
        builder.Property(o => o.TrackingProvider).HasMaxLength(100);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);
        builder.Property(o => o.CreatedAtUtc).IsRequired();
        builder.Property(o => o.UpdatedAtUtc).IsRequired();

        // Shadow property for checkout idempotency key
        builder.Property<string?>("IdempotencyKey").HasMaxLength(128);

        // ShippingAddress as owned entity — flattened columns
        builder.OwnsOne(o => o.ShippingAddress, addr =>
        {
            addr.Property(a => a.FullName).HasColumnName("shipping_full_name").IsRequired().HasMaxLength(200);
            addr.Property(a => a.Phone).HasColumnName("shipping_phone").IsRequired().HasMaxLength(20);
            addr.Property(a => a.AddressLine1).HasColumnName("shipping_address_line1").IsRequired().HasMaxLength(300);
            addr.Property(a => a.AddressLine2).HasColumnName("shipping_address_line2").HasMaxLength(300);
            addr.Property(a => a.City).HasColumnName("shipping_city").IsRequired().HasMaxLength(100);
            addr.Property(a => a.State).HasColumnName("shipping_state").IsRequired().HasMaxLength(100);
            addr.Property(a => a.PostalCode).HasColumnName("shipping_postal_code").IsRequired().HasMaxLength(20);
            addr.Property(a => a.Country).HasColumnName("shipping_country").IsRequired().HasMaxLength(2);
        });

        builder.HasIndex(o => o.CustomerId).HasDatabaseName("ix_orders_customer");
        builder.HasIndex(o => o.Status).HasDatabaseName("ix_orders_status");
        builder.HasIndex(o => o.OrderNumber).IsUnique().HasDatabaseName("ix_orders_number");
        builder.HasIndex(o => new { o.CustomerId, o.Status }).HasDatabaseName("ix_orders_customer_status");
        builder.HasIndex("IdempotencyKey")
            .HasFilter("\"IdempotencyKey\" IS NOT NULL")
            .HasDatabaseName("ix_orders_idempotency_key");

        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductName).IsRequired().HasMaxLength(500);
        builder.Property(i => i.VariantDescription).HasMaxLength(500);
        builder.Property(i => i.Sku).HasMaxLength(100);
        builder.Property(i => i.UnitPrice).IsRequired().HasColumnType("numeric(12,2)");
        builder.Property(i => i.Quantity).IsRequired();
        builder.Property(i => i.LineTotal).IsRequired().HasColumnType("numeric(12,2)");

        builder.HasIndex(i => i.OrderId).HasDatabaseName("ix_order_items_order");
    }
}
