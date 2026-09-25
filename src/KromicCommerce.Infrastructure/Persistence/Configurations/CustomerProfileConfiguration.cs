using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class CustomerProfileConfiguration : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        builder.ToTable("customer_profiles");
        builder.HasKey(cp => cp.Id);

        builder.Property(cp => cp.DisplayName).HasMaxLength(100);
        builder.Property(cp => cp.AvatarUrl).HasMaxLength(1024);
        builder.Property(cp => cp.PhoneNumber).HasMaxLength(20);
        builder.Property(cp => cp.NewsletterConsent).IsRequired().HasDefaultValue(false);
        builder.Property(cp => cp.PreferredTimeZoneId).HasMaxLength(100);
        builder.Property(cp => cp.CreatedAtUtc).IsRequired();
        builder.Property(cp => cp.UpdatedAtUtc).IsRequired();

        builder.HasIndex(cp => cp.UserId).IsUnique().HasDatabaseName("ix_customer_profiles_user_id");
    }
}
