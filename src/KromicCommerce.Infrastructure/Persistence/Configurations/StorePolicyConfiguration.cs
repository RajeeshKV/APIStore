using KromicCommerce.Domain.Store;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class StorePolicyConfiguration : IEntityTypeConfiguration<StorePolicy>
{
    public void Configure(EntityTypeBuilder<StorePolicy> builder)
    {
        builder.ToTable("store_policies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PolicyType)
            .IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(p => p.Title).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Content).IsRequired(); // no length limit for policy text
        builder.Property(p => p.IsPublished).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        // At most one policy per type — enforced at DB level
        builder.HasIndex(p => p.PolicyType)
            .IsUnique().HasDatabaseName("ix_store_policies_type");

        builder.HasIndex(p => p.IsPublished).HasDatabaseName("ix_store_policies_published");
    }
}
