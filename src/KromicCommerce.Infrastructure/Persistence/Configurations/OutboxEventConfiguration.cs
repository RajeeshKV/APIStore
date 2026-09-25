using KromicCommerce.Domain.Outbox;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Payload).IsRequired(); // JSON, no length limit
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.RetryCount).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.Error).HasMaxLength(1000);

        // Polling index: unprocessed events ordered by creation time
        builder.HasIndex(e => new { e.ProcessedAt, e.CreatedAt, e.RetryCount })
            .HasDatabaseName("ix_outbox_events_pending");
    }
}
