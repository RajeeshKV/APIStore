using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Identity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class ProductReviewConfiguration : IEntityTypeConfiguration<ProductReview>
{
    public void Configure(EntityTypeBuilder<ProductReview> builder)
    {
        builder.ToTable("product_reviews", t =>
            // Defence in depth behind the domain guard: a direct SQL write or a future
            // code path that skips Create/Edit validation still cannot store a 0 or 6.
            t.HasCheckConstraint("ck_product_reviews_rating", "\"Rating\" BETWEEN 1 AND 5"));
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Rating).IsRequired();

        builder.Property(r => r.Title).HasMaxLength(ProductReview.TitleMaxLength);
        builder.Property(r => r.Body).IsRequired().HasMaxLength(ProductReview.BodyMaxLength);
        builder.Property(r => r.IsVerifiedPurchase).IsRequired().HasDefaultValue(false);
        builder.Property(r => r.Status)
            .IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(r => r.ModerationReason)
            .HasMaxLength(ProductReview.ModerationReasonMaxLength);
        builder.Property(r => r.HelpfulCount).IsRequired().HasDefaultValue(0);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();

        // One review per customer per product/variant.
        //
        // Declared as an ordinary unique index because EF Core cannot express NULLS NOT
        // DISTINCT; the migration creates the real index with that clause via raw SQL. Without
        // it, NULL ProductVariantId counts as distinct and a customer could post unlimited
        // product-level reviews. This index is the concurrency guard for duplicate submits —
        // a handler pre-check loses the race.
        builder.HasIndex(r => new { r.CustomerId, r.ProductId, r.ProductVariantId })
            .IsUnique()
            .HasDatabaseName("ix_product_reviews_customer_product_variant");

        // Covers the public listing: filter to a product, keep only published, order by recency.
        builder.HasIndex(r => new { r.ProductId, r.Status, r.PublishedAtUtc })
            .HasDatabaseName("ix_product_reviews_product_status_published");

        // Covers "my reviews" and the admin moderation queue.
        builder.HasIndex(r => new { r.Status, r.CreatedAtUtc })
            .HasDatabaseName("ix_product_reviews_status_created");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // The ProductId foreign key is configured from ProductConfiguration, which owns the
        // whole set of Product-owned collections. Declaring it here as well produced a
        // duplicate FK (…_ProductId1) and a redundant index.
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(r => r.ProductVariantId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(r => r.Images)
            .WithOne(i => i.Review)
            .HasForeignKey(i => i.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ReviewImageConfiguration : IEntityTypeConfiguration<ReviewImage>
{
    public void Configure(EntityTypeBuilder<ReviewImage> builder)
    {
        builder.ToTable("review_images");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.SortOrder).IsRequired().HasDefaultValue(0);
        builder.Property(i => i.CreatedAt).IsRequired();

        builder.HasIndex(i => new { i.ReviewId, i.SortOrder })
            .HasDatabaseName("ix_review_images_review_sort");

        // Owned MediaAsset — flattened columns. Identical mapping to ProductImageConfiguration.
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

internal sealed class ReviewHelpfulVoteConfiguration : IEntityTypeConfiguration<ReviewHelpfulVote>
{
    public void Configure(EntityTypeBuilder<ReviewHelpfulVote> builder)
    {
        builder.ToTable("review_helpful_votes");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.CreatedAt).IsRequired();

        // Makes the helpful toggle safe under concurrent double-taps: one vote per customer
        // per review. Exceeding it is caught and translated into an idempotent toggle result.
        builder.HasIndex(v => new { v.ReviewId, v.CustomerId })
            .IsUnique()
            .HasDatabaseName("ix_review_helpful_votes_review_customer");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(v => v.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configured here rather than from ProductReview: there is no HelpfulVotes navigation on
        // the review, and a skip-navigation HasMany cannot take a HasForeignKey expression.
        builder.HasOne<ProductReview>()
            .WithMany()
            .HasForeignKey(v => v.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}