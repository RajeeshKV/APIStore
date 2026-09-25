using KromicCommerce.Domain.Promotions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("promotions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);

        builder.Property(p => p.CouponCode).IsRequired().HasMaxLength(50);
        builder.HasIndex(p => p.CouponCode)
            .IsUnique()
            .HasDatabaseName("ix_promotions_coupon_code");

        builder.Property(p => p.DiscountType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(p => p.DiscountValue)
            .IsRequired()
            .HasColumnType("numeric(10,4)");

        builder.Property(p => p.MaxDiscountAmount)
            .HasColumnType("numeric(12,2)");

        builder.Property(p => p.MinimumOrderAmount)
            .HasColumnType("numeric(12,2)");

        builder.Property(p => p.Applicability)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(p => p.IsFirstOrderOnly).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.UsageCount).IsRequired();
        builder.Property(p => p.UsageLimit);
        builder.Property(p => p.PerCustomerUsageLimit);
        builder.Property(p => p.StartsAt);
        builder.Property(p => p.ExpiresAt);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        // xmin concurrency token — PostgreSQL system column that changes on every UPDATE.
        // Prevents concurrent usage-limit races: two checkouts loading the same row will have
        // the same xmin; the second SaveChangesAsync throws DbUpdateConcurrencyException.
        builder.Property(p => p.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        builder.HasIndex(p => p.IsActive)
            .HasDatabaseName("ix_promotions_active");
        builder.HasIndex(p => p.ExpiresAt)
            .HasDatabaseName("ix_promotions_expires");

        builder.HasMany(p => p.Products)
            .WithOne(pp => pp.Promotion)
            .HasForeignKey(pp => pp.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Categories)
            .WithOne(pc => pc.Promotion)
            .HasForeignKey(pc => pc.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.Products).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.Categories).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PromotionProductConfiguration : IEntityTypeConfiguration<PromotionProduct>
{
    public void Configure(EntityTypeBuilder<PromotionProduct> builder)
    {
        builder.ToTable("promotion_products");
        builder.HasKey(pp => new { pp.PromotionId, pp.ProductId });
        builder.Property(pp => pp.PromotionId).IsRequired();
        builder.Property(pp => pp.ProductId).IsRequired();
        builder.HasIndex(pp => pp.PromotionId)
            .HasDatabaseName("ix_promotion_products_promotion");
    }
}

internal sealed class PromotionCategoryConfiguration : IEntityTypeConfiguration<PromotionCategory>
{
    public void Configure(EntityTypeBuilder<PromotionCategory> builder)
    {
        builder.ToTable("promotion_categories");
        builder.HasKey(pc => new { pc.PromotionId, pc.CategoryId });
        builder.Property(pc => pc.PromotionId).IsRequired();
        builder.Property(pc => pc.CategoryId).IsRequired();
        builder.HasIndex(pc => pc.PromotionId)
            .HasDatabaseName("ix_promotion_categories_promotion");
    }
}

internal sealed class PromotionUsageConfiguration : IEntityTypeConfiguration<PromotionUsage>
{
    public void Configure(EntityTypeBuilder<PromotionUsage> builder)
    {
        builder.ToTable("promotion_usages");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.PromotionId).IsRequired();
        builder.Property(u => u.CustomerId).IsRequired();
        builder.Property(u => u.OrderId).IsRequired();
        builder.Property(u => u.UsedAt).IsRequired();

        // Unique: one usage row per promotion per order
        builder.HasIndex(u => new { u.PromotionId, u.OrderId })
            .IsUnique()
            .HasDatabaseName("ix_promotion_usages_promotion_order");

        builder.HasIndex(u => new { u.PromotionId, u.CustomerId })
            .HasDatabaseName("ix_promotion_usages_promotion_customer");

        builder.HasIndex(u => u.CustomerId)
            .HasDatabaseName("ix_promotion_usages_customer");

        builder.HasOne(u => u.Promotion)
            .WithMany()
            .HasForeignKey(u => u.PromotionId)
            .OnDelete(DeleteBehavior.Restrict); // preserve usage history
    }
}
