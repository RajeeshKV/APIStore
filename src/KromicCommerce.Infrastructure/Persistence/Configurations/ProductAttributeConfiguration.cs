using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class ProductAttributeConfiguration : IEntityTypeConfiguration<ProductAttribute>
{
    public void Configure(EntityTypeBuilder<ProductAttribute> builder)
    {
        builder.ToTable("product_attributes");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(100);
        builder.Property(a => a.SortOrder).IsRequired().HasDefaultValue(0);
        builder.HasIndex(a => new { a.ProductId, a.Name }).HasDatabaseName("ix_product_attributes_product_name");

        builder.HasMany(a => a.Values)
            .WithOne(v => v.Attribute).HasForeignKey(v => v.AttributeId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Values).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProductAttributeValueConfiguration : IEntityTypeConfiguration<ProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProductAttributeValue> builder)
    {
        builder.ToTable("product_attribute_values");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Value).IsRequired().HasMaxLength(200);
        builder.Property(v => v.SortOrder).IsRequired().HasDefaultValue(0);
        builder.HasIndex(v => new { v.AttributeId, v.Value }).HasDatabaseName("ix_product_attribute_values_attr_value");
    }
}
