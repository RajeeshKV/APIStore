using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> builder)
    {
        builder.ToTable("wishlist_items");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        // Enforces "one wishlist entry per customer per product/variant".
        //
        // This is declared as an ordinary unique index because EF Core cannot express
        // NULLS NOT DISTINCT. The migration creates the real index with that clause via raw SQL —
        // the same approach already used for ix_cart_items_cart_product_variant and
        // ix_inventory_items_product_variant. Without the clause PostgreSQL treats NULL
        // ProductVariantId as distinct, so one customer could save a product-level wishlist
        // entry any number of times. Do not let EF recreate this index from the model.
        builder.HasIndex(w => new { w.CustomerId, w.ProductId, w.ProductVariantId })
            .IsUnique()
            .HasDatabaseName("ix_wishlist_items_customer_product_variant");

        // Covers the ordered customer listing (CustomerId + CreatedAtUtc).
        builder.HasIndex(w => new { w.CustomerId, w.CreatedAtUtc })
            .HasDatabaseName("ix_wishlist_items_customer_created");

        // Covers the per-product membership check used by product cards.
        builder.HasIndex(w => w.ProductId)
            .HasDatabaseName("ix_wishlist_items_product");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(w => w.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a variant must not silently delete the customer's whole wishlist entry;
        // the entry survives and simply loses its variant scope.
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(w => w.ProductVariantId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}