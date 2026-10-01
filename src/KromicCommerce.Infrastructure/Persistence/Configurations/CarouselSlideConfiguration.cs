using KromicCommerce.Domain.Catalog;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class CarouselSlideConfiguration : IEntityTypeConfiguration<CarouselSlide>
{
    public void Configure(EntityTypeBuilder<CarouselSlide> builder)
    {
        builder.ToTable("carousel_slides");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Subtitle).HasMaxLength(500);
        builder.Property(s => s.ImagePublicId).HasMaxLength(512);
        builder.Property(s => s.ImageUrl).HasMaxLength(1024);
        builder.Property(s => s.CtaText).HasMaxLength(50);
        builder.Property(s => s.SortOrder).IsRequired().HasDefaultValue(0);
        builder.Property(s => s.IsActive).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();

        // The public storefront query filters on IsActive and orders by SortOrder on every
        // request, so the composite index matches that access path rather than either column alone.
        builder.HasIndex(s => new { s.IsActive, s.SortOrder })
            .HasDatabaseName("ix_carousel_slides_active_sortorder");
    }
}