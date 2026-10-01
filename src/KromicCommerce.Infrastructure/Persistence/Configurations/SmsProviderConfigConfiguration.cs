using KromicCommerce.Domain.Sms;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class SmsProviderConfigConfiguration : IEntityTypeConfiguration<SmsProviderConfig>
{
    public void Configure(EntityTypeBuilder<SmsProviderConfig> builder)
    {
        builder.ToTable("sms_provider_configs");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Enabled).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.Provider).IsRequired().HasMaxLength(50).HasDefaultValue("None");
        builder.Property(c => c.EncryptedSettings).IsRequired().HasDefaultValue("{}");
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();
    }
}
