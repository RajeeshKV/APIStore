using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("brands");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Name).IsRequired().HasMaxLength(200);
        builder.Property(b => b.Slug).IsRequired().HasMaxLength(200);
        builder.Property(b => b.Description).HasMaxLength(2000);
        builder.Property(b => b.WebsiteUrl).HasMaxLength(512);
        builder.Property(b => b.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(b => b.LogoPublicId).HasMaxLength(512);
        builder.Property(b => b.LogoUrl).HasMaxLength(1024);
        builder.Property(b => b.CreatedAtUtc).IsRequired();
        builder.Property(b => b.UpdatedAtUtc).IsRequired();

        builder.HasIndex(b => b.Slug).IsUnique().HasDatabaseName("ix_brands_slug");
    }
}
