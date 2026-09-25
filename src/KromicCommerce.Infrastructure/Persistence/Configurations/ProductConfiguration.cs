using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(500);
        builder.Property(p => p.Slug).IsRequired().HasMaxLength(500);
        builder.Property(p => p.Sku).HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(10000);
        builder.Property(p => p.ShortDescription).HasMaxLength(500);
        builder.Property(p => p.Price).IsRequired().HasColumnType("numeric(12,2)");
        builder.Property(p => p.CompareAtPrice).HasColumnType("numeric(12,2)");
        builder.Property(p => p.Status)
            .IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(p => p.MetaTitle).HasMaxLength(120);
        builder.Property(p => p.MetaDescription).HasMaxLength(320);
        builder.Property(p => p.MetaKeywords).HasMaxLength(500);
        builder.Property(p => p.IsFeatured).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.IsTaxable).IsRequired().HasDefaultValue(true);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasIndex(p => p.Slug).IsUnique().HasDatabaseName("ix_products_slug");
        builder.HasIndex(p => p.Sku)
            .IsUnique().HasFilter("sku IS NOT NULL").HasDatabaseName("ix_products_sku");
        builder.HasIndex(p => p.Status).HasDatabaseName("ix_products_status");
        builder.HasIndex(p => p.CategoryId).HasDatabaseName("ix_products_category");
        builder.HasIndex(p => p.BrandId).HasDatabaseName("ix_products_brand");

        builder.HasOne(p => p.Category)
            .WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(p => p.Brand)
            .WithMany().HasForeignKey(p => p.BrandId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Images)
            .WithOne(i => i.Product).HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Attributes)
            .WithOne(a => a.Product).HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Variants)
            .WithOne(v => v.Product).HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Attributes).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
