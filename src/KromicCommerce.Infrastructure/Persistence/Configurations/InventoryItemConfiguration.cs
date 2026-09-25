using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.OnHand).IsRequired().HasDefaultValue(0);
        builder.Property(i => i.Reserved).IsRequired().HasDefaultValue(0);
        builder.Property(i => i.LowStockThreshold).IsRequired().HasDefaultValue(5);
        builder.Property(i => i.UpdatedAt).IsRequired();

        // PostgreSQL xmin concurrency token — prevents overselling.
        // Maps the built-in xmin system column (uint) as a row-version concurrency token.
        // UseXminAsConcurrencyToken() is obsolete in Npgsql EF 8; the equivalent is:
        builder.Property(i => i.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasIndex(i => new { i.ProductId, i.VariantId })
            .IsUnique().HasDatabaseName("ix_inventory_items_product_variant");

        builder.HasOne(i => i.Product)
            .WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Variant)
            .WithMany().HasForeignKey(i => i.VariantId).OnDelete(DeleteBehavior.Cascade);
    }
}
