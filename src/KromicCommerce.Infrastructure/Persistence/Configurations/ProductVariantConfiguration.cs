using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Sku).HasMaxLength(100);
        builder.Property(v => v.PriceOverride).HasColumnType("numeric(12,2)");
        builder.Property(v => v.SortOrder).IsRequired().HasDefaultValue(0);
        builder.Property(v => v.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(v => v.AttributeValueIds).HasMaxLength(1000);
        builder.Property(v => v.CreatedAtUtc).IsRequired();
        builder.Property(v => v.UpdatedAtUtc).IsRequired();

        builder.HasIndex(v => v.Sku)
            .IsUnique().HasFilter("\"Sku\" IS NOT NULL").HasDatabaseName("ix_product_variants_sku");
        builder.HasIndex(v => v.ProductId).HasDatabaseName("ix_product_variants_product");
    }
}
