using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class CartConfiguration : IEntityTypeConfiguration<KromicCommerce.Domain.Cart.Cart>
{
    public void Configure(EntityTypeBuilder<KromicCommerce.Domain.Cart.Cart> builder)
    {
        builder.ToTable("carts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AnonymousId).HasMaxLength(128);
        builder.Property(c => c.ExpiresAt).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        builder.HasIndex(c => c.CustomerId)
            .HasFilter("customer_id IS NOT NULL")
            .HasDatabaseName("ix_carts_customer");
        builder.HasIndex(c => c.AnonymousId)
            .HasFilter("anonymous_id IS NOT NULL")
            .HasDatabaseName("ix_carts_anonymous");

        builder.HasMany(c => c.Items)
            .WithOne(i => i.Cart)
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CartItemConfiguration : IEntityTypeConfiguration<KromicCommerce.Domain.Cart.CartItem>
{
    public void Configure(EntityTypeBuilder<KromicCommerce.Domain.Cart.CartItem> builder)
    {
        builder.ToTable("cart_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Quantity).IsRequired();
        builder.Property(i => i.AddedAt).IsRequired();

        builder.HasIndex(i => new { i.CartId, i.ProductId, i.VariantId })
            .HasDatabaseName("ix_cart_items_cart_product_variant");
    }
}
