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
