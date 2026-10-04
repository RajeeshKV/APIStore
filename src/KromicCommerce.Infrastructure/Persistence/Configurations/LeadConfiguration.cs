using KromicCommerce.Domain.Leads;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

/// <summary>
/// Persistence mapping for captured leads.
///
/// <para>
/// <see cref="Lead.Phone"/> is unique across non-archived rows. The public form is reachable by
/// anyone and mail is delivered to a real inbox, so a repeat submission is worth stopping: it is
/// usually a double-click, a lost response, or someone probing the endpoint. The partial
/// predicate means a genuine repeat enquiry can still be recorded after an earlier lead is
/// archived, rather than the second submission being permanently rejected.
/// </para>
/// </summary>
public sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("leads");
        builder.HasKey(x => x.Id);

        // Server assigned timestamps: a lead is created by an anonymous public request, so there
        // is no authenticated actor to record. CreatedBy stays null rather than inventing one.
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(x => x.Name).HasMaxLength(Lead.NameMaxLength).IsRequired();
        builder.Property(x => x.PhoneRaw).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(Lead.EmailMaxLength).IsRequired();
        builder.Property(x => x.Business).HasMaxLength(Lead.BusinessMaxLength).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(200);
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.Property(x => x.UserAgent).HasMaxLength(400);

        // Stored as a string, matching every other enum in this model, so the value stays
        // readable in psql and adding a status cannot renumber existing rows.
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Ignore(x => x.NotifiedAt);

        builder.HasIndex(x => x.CreatedAtUtc).HasDatabaseName("ix_leads_created_at");

        builder.HasIndex(x => x.Status).HasDatabaseName("ix_leads_status");

        // Paging with no filter is ordered by CreatedAtUtc DESC, so this index serves the
        // default admin list directly.
        builder.HasIndex(x => new { x.IsArchived, x.CreatedAtUtc })
            .HasDatabaseName("ix_leads_archived_created");

        builder.HasIndex(x => x.Phone)
            .IsUnique()
            .HasDatabaseName("ux_leads_phone_active")
            .HasFilter("\"IsArchived\" = false");
    }
}