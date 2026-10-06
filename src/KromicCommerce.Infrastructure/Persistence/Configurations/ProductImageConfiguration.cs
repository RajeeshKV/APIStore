using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("product_images");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.SortOrder).IsRequired().HasDefaultValue(0);
        builder.Property(i => i.IsPrimary).IsRequired().HasDefaultValue(false);
        builder.Property(i => i.CreatedAt).IsRequired();

        builder.HasIndex(i => new { i.ProductId, i.SortOrder }).HasDatabaseName("ix_product_images_product_sort");

        // Variant-scoped images are unique per (Product, Variant, SortOrder) so a variant's
        // gallery cannot accumulate duplicate positions. Product-level rows are excluded by the
        // filter: a product's own gallery is not constrained this way.
        builder.HasIndex(i => new { i.ProductId, i.VariantId, i.SortOrder })
            .HasDatabaseName("ix_product_images_variant_sort")
            .HasFilter("\"VariantId\" IS NOT NULL");

        // Owned MediaAsset — flattened columns
        builder.OwnsOne(i => i.Asset, a =>
        {
            a.Property(x => x.PublicId).HasColumnName("asset_public_id").IsRequired().HasMaxLength(512);
            a.Property(x => x.SecureUrl).HasColumnName("asset_secure_url").IsRequired().HasMaxLength(1024);
            a.Property(x => x.Format).HasColumnName("asset_format").HasMaxLength(20);
            a.Property(x => x.Width).HasColumnName("asset_width");
            a.Property(x => x.Height).HasColumnName("asset_height");
            a.Property(x => x.AltText).HasColumnName("asset_alt_text").HasMaxLength(500);
        });
    }
}
