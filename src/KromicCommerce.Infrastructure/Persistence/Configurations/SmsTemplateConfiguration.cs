using KromicCommerce.Domain.Sms;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class SmsTemplateConfiguration : IEntityTypeConfiguration<SmsTemplate>
{
    public void Configure(EntityTypeBuilder<SmsTemplate> builder)
    {
        builder.ToTable("sms_templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Provider)
            .IsRequired().HasConversion<string>().HasMaxLength(50);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Body).IsRequired();
        builder.Property(t => t.ExternalTemplateId).HasMaxLength(100);
        builder.Property(t => t.IsActive).IsRequired().HasDefaultValue(false);
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();

        // At most one ACTIVE template per provider. Non-active templates are kept so switching
        // back does not require re-entering a registration. A plain unique index on Provider
        // would forbid keeping a history, so the filter is on the active flag instead.
        builder.HasIndex(t => new { t.Provider, t.IsActive })
            .IsUnique()
            .HasFilter("\"IsActive\" = true")
            .HasDatabaseName("ix_sms_templates_active_per_provider");
    }
}
