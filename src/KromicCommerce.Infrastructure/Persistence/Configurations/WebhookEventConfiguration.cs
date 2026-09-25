using KromicCommerce.Domain.Webhooks;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(50);
        builder.Property(e => e.ProviderEventId).IsRequired().HasMaxLength(200);
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Payload).IsRequired(); // raw JSON
        builder.Property(e => e.ReceivedAt).IsRequired();

        // Unique idempotency index: provider + event ID
        builder.HasIndex(e => new { e.Provider, e.ProviderEventId })
            .IsUnique()
            .HasDatabaseName("ix_webhook_events_provider_event");
    }
}
