using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Support;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets", t =>
        {
            // Defence in depth behind the domain guards. A direct SQL write or a future code
            // path that bypasses Ticket.Resolve cannot store a negative deadline.
            t.HasCheckConstraint("ck_tickets_reopen_count", "\"ReopenCount\" >= 0");
            t.HasCheckConstraint("ck_tickets_idle_hours", "\"AutoCloseIdleHours\" >= 1");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TicketNumber).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(Ticket.SubjectMaxLength);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(Ticket.DescriptionMaxLength);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Priority).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ReopenCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.AutoCloseIdleHours).IsRequired().HasDefaultValue(72);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.LastActivityAtUtc).IsRequired();
        builder.Property(x => x.LastUserActivityAtUtc).IsRequired();

        // The human-facing reference. Unique so it can be quoted by customers in email and
        // so the ticket-number generator can rely on a collision being detectable.
        builder.HasIndex(x => x.TicketNumber)
            .IsUnique()
            .HasDatabaseName("ix_tickets_ticket_number");

        // Backs the auto-close worker's scan. The worker asks for
        //   Status = 'Resolved' AND AutoCloseAtUtc <= now
        // and nothing else, so this partial index is the whole query.
        builder.HasIndex(x => x.AutoCloseAtUtc)
            .HasDatabaseName("ix_tickets_autoclose_deadline")
            .HasFilter("\"AutoCloseAtUtc\" IS NOT NULL AND \"Status\" = 'Resolved'");

        // "My tickets" list, newest activity first. Also the default ordering for the
        // customer's ticket screen, so it is worth covering fully.
        builder.HasIndex(x => new { x.CustomerId, x.LastActivityAtUtc })
            .HasDatabaseName("ix_tickets_customer_activity");

        // Admin queue: filter by status, then order by activity. Ordered descending because
        // PostgreSQL scans a b-tree backwards just as efficiently as forwards.
        builder.HasIndex(x => new { x.Status, x.LastActivityAtUtc })
            .HasDatabaseName("ix_tickets_status_activity");

        // Unanswered-ticket queue: Open tickets with no admin reply yet, oldest first so the
        // longest-waiting customer is at the top.
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc })
            .HasDatabaseName("ix_tickets_status_created");

        // Linked-order lookup, used by the invoice composer and the order detail screen.
        builder.HasIndex(x => x.RelatedOrderId)
            .HasDatabaseName("ix_tickets_related_order");

        // Navigation-based HasOne, not HasOne<User>(). Naming the navigation is what tells EF
        // to reuse the CustomerId property; the parameterless form claims CustomerId for a
        // navigation-less relationship and convention then invents a second, shadow "CustomerId1"
        // foreign key for the Customer navigation — two FKs to the same column.
        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // No navigation for the assignee, so there is no second relationship to confuse.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.AssignedAdminId)
            .OnDelete(DeleteBehavior.SetNull);

        // SetNull, not Cascade. The conversation is the durable record of what the customer
        // was told about that order; deleting an order must not delete the support history.
        builder.HasOne(x => x.RelatedOrder)
            .WithMany()
            .HasForeignKey(x => x.RelatedOrderId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Comments)
            .WithOne(c => c.Ticket)
            .HasForeignKey(c => c.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        // The audit log is configured from the principal side only. TicketStatusHistory also
        // carries a Ticket navigation, and if that navigation got its own HasOne, convention
        // would already have claimed TicketId for the collection relationship above — leaving
        // the navigation to invent a second, shadow "TicketId1" foreign key.
        builder.HasMany(x => x.History)
            .WithOne(h => h.Ticket)
            .HasForeignKey(h => h.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Comments).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("ticket_comments", t =>
        {
            t.HasCheckConstraint("ck_ticket_comments_depth", $"\"Depth\" >= 0 AND \"Depth\" <= {TicketComment.MaxDepth}");

            // A comment must hang off an existing comment or sit at the root. Enforced in the
            // database as well as by TicketComment.Create, so a corrupted parent cannot be
            // introduced by a background script or a manual fix.
            t.HasCheckConstraint(
                "ck_ticket_comments_parent_link",
                $"\"Depth\" = 0 AND \"ParentCommentId\" IS NULL OR \"Depth\" > 0 AND \"ParentCommentId\" IS NOT NULL");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).IsRequired().HasMaxLength(TicketComment.BodyMaxLength);
        builder.Property(c => c.IsAdminAuthor).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.InternalNote).IsRequired().HasDefaultValue(false);
        builder.Property(c => c.Depth).IsRequired().HasDefaultValue(0);

        // 19 ticks digits plus 6 levels of '/'. 128 is comfortable headroom without
        // wandering into TEXT on every row.
        builder.Property(c => c.ThreadPath).IsRequired().HasMaxLength(128);

        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        // THE thread index. Ordering by ThreadPath alone returns the entire conversation in
        // reading order, so this is the only index the read path needs.
        builder.HasIndex(c => new { c.TicketId, c.ThreadPath })
            .IsUnique()
            .HasDatabaseName("ix_ticket_comments_ticket_path");

        // Direct-children lookup and subtree prefix scans.
        builder.HasIndex(c => c.ParentCommentId)
            .HasDatabaseName("ix_ticket_comments_parent");

        builder.HasOne(c => c.Parent)
            .WithMany()
            .HasForeignKey(c => c.ParentCommentId)
            // Restrict, not Cascade. Cascading a self-referencing delete would remove an
            // entire reply branch when a single parent row is removed, and the parent is
            // only ever removed together with its whole ticket.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(c => c.Attachments)
            .WithOne(a => a.Comment)
            .HasForeignKey(a => a.TicketCommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(c => c.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("ticket_attachments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.PublicId).IsRequired().HasMaxLength(TicketAttachment.PublicIdMaxLength);
        builder.Property(a => a.SecureUrl).IsRequired().HasMaxLength(TicketAttachment.UrlMaxLength);
        builder.Property(a => a.Format).HasMaxLength(20);
        builder.Property(a => a.ContentType).HasMaxLength(120);
        builder.Property(a => a.AltText).HasMaxLength(TicketAttachment.AltTextMaxLength);
        builder.Property(a => a.SizeBytes).IsRequired();
        builder.Property(a => a.SortOrder).IsRequired().HasDefaultValue(0);
        builder.Property(a => a.CreatedAtUtc).IsRequired();
        builder.Property(a => a.UpdatedAtUtc).IsRequired();

        builder.HasIndex(a => new { a.TicketCommentId, a.SortOrder })
            .HasDatabaseName("ix_ticket_attachments_comment_sort");
    }
}

internal sealed class TicketStatusHistoryConfiguration : IEntityTypeConfiguration<TicketStatusHistory>
{
    public void Configure(EntityTypeBuilder<TicketStatusHistory> builder)
    {
        builder.ToTable("ticket_status_history");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.ToStatus).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(h => h.Actor).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(h => h.ActorName).HasMaxLength(TicketStatusHistory.ActorNameMaxLength);
        builder.Property(h => h.Note).HasMaxLength(TicketStatusHistory.NoteMaxLength);
        builder.Property(h => h.OccurredAtUtc).IsRequired();
        builder.Property(h => h.CreatedAtUtc).IsRequired();
        builder.Property(h => h.UpdatedAtUtc).IsRequired();

        // The audit read path: one ticket's history in order.
        builder.HasIndex(h => new { h.TicketId, h.OccurredAtUtc })
            .HasDatabaseName("ix_ticket_status_history_ticket_occurred");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(h => h.ActorId)
            .OnDelete(DeleteBehavior.SetNull);

        // The Ticket relationship is configured on TicketConfiguration as
        // HasMany(History).WithOne().HasForeignKey(TicketId), so it is deliberately not
        // repeated here — a second HasOne on the Ticket navigation would duplicate the FK.
    }
}

internal sealed class TicketInvoiceConfiguration : IEntityTypeConfiguration<TicketInvoice>
{
    public void Configure(EntityTypeBuilder<TicketInvoice> builder)
    {
        builder.ToTable("ticket_invoices", t =>
        {
            t.HasCheckConstraint("ck_ticket_invoices_revision", "\"Revision\" >= 1");
            t.HasCheckConstraint("ck_ticket_invoices_attempts",
                $"\"GenerationAttempts\" >= 0 AND \"GenerationAttempts\" <= {TicketInvoice.MaxGenerationAttempts}");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(TicketInvoice.NumberMaxLength);
        builder.Property(i => i.Revision).IsRequired();
        builder.Property(i => i.Status).IsRequired().HasConversion<string>().HasMaxLength(24);
        builder.Property(i => i.GenerationAttempts).IsRequired().HasDefaultValue(0);
        builder.Property(i => i.IsContentOverridden).IsRequired().HasDefaultValue(false);
        builder.Property(i => i.EmailedToCustomer).IsRequired().HasDefaultValue(false);
        builder.Property(i => i.FileName).IsRequired().HasMaxLength(200);
        builder.Property(i => i.SizeBytes).IsRequired().HasDefaultValue(0L);
        builder.Property(i => i.Checksum).HasMaxLength(64);
        builder.Property(i => i.FailureReason).HasMaxLength(2000);
        builder.Property(i => i.QueuedAtUtc).IsRequired();
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.UpdatedAtUtc).IsRequired();

        // Uniqueness is (TicketId, Revision) AND InvoiceNumber. The invoice number is the
        // external reference printed on the document, so it must never repeat either.
        builder.HasIndex(i => new { i.TicketId, i.Revision })
            .IsUnique()
            .HasDatabaseName("ix_ticket_invoices_ticket_revision");

        builder.HasIndex(i => i.InvoiceNumber)
            .IsUnique()
            .HasDatabaseName("ix_ticket_invoices_invoice_number");

        // The render worker's claim query. Ordering by QueuedAtUtc inside a ticket revision
        // keeps repeated enqueues deterministic.
        builder.HasIndex(i => new { i.Status, i.QueuedAtUtc })
            .HasDatabaseName("ix_ticket_invoices_status_queued");

        // Owned InvoiceContent — flattened into this table. No join on the read path and no
        // separate table for a value that is only ever read with its invoice.
        builder.OwnsOne(i => i.Content, c =>
        {
            c.Property(x => x.IssuerName).HasColumnName("issuer_name").IsRequired().HasMaxLength(200);
            c.Property(x => x.IssuerAddress).HasColumnName("issuer_address").HasMaxLength(300);
            c.Property(x => x.IssuerEmail).HasColumnName("issuer_email").HasMaxLength(300);
            c.Property(x => x.IssuerTaxId).HasColumnName("issuer_tax_id").HasMaxLength(120);
            c.Property(x => x.BillToName).HasColumnName("bill_to_name").IsRequired().HasMaxLength(300);
            c.Property(x => x.BillToEmail).HasColumnName("bill_to_email").HasMaxLength(300);
            c.Property(x => x.BillToAddress).HasColumnName("bill_to_address").HasMaxLength(300);
            c.Property(x => x.InvoiceDateUtc).HasColumnName("invoice_date_utc").IsRequired();
            c.Property(x => x.DueDateUtc).HasColumnName("due_date_utc");
            c.Property(x => x.CurrencyCode).HasColumnName("currency_code").IsRequired().HasMaxLength(3);
            c.Property(x => x.Subtotal).HasColumnName("subtotal").IsRequired();
            c.Property(x => x.DiscountAmount).HasColumnName("discount_amount").IsRequired();
            c.Property(x => x.TaxAmount).HasColumnName("tax_amount").IsRequired();
            c.Property(x => x.ShippingAmount).HasColumnName("shipping_amount").IsRequired();
            c.Property(x => x.CodFee).HasColumnName("cod_fee").IsRequired();
            c.Property(x => x.GrandTotal).HasColumnName("grand_total").IsRequired();
            c.Property(x => x.LineItemsJson).HasColumnName("line_items_json").IsRequired();
            c.Property(x => x.TicketNumber).HasColumnName("ticket_number").IsRequired().HasMaxLength(64);
            c.Property(x => x.TicketSubject).HasColumnName("ticket_subject").HasMaxLength(200);
            c.Property(x => x.OrderNumber).HasColumnName("order_number").HasMaxLength(100);
            c.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(TicketInvoice.NotesMaxLength);
            c.Property(x => x.Terms).HasColumnName("terms").HasMaxLength(TicketInvoice.TermsMaxLength);
            c.Property(x => x.FooterNote).HasColumnName("footer_note").HasMaxLength(TicketInvoice.FooterMaxLength);
            c.Property(x => x.AccentColor).HasColumnName("accent_color").IsRequired().HasMaxLength(6);
        });

        builder.Navigation(i => i.Content).IsRequired();

        builder.HasOne(i => i.Ticket)
            .WithMany()
            .HasForeignKey(i => i.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.CreatedByAdminId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.OverriddenByAdminId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<InvoiceTemplate>()
            .WithMany()
            .HasForeignKey(i => i.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class InvoiceTemplateConfiguration : IEntityTypeConfiguration<InvoiceTemplate>
{
    public void Configure(EntityTypeBuilder<InvoiceTemplate> builder)
    {
        builder.ToTable("invoice_templates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(InvoiceTemplate.NameMaxLength);
        builder.Property(t => t.Description).HasMaxLength(InvoiceTemplate.DescriptionMaxLength);
        builder.Property(t => t.IsDefault).IsRequired().HasDefaultValue(false);
        builder.Property(t => t.IssuerNameOverride).HasMaxLength(InvoiceTemplate.IssuerNameMaxLength);
        builder.Property(t => t.TaxId).HasMaxLength(InvoiceTemplate.TaxIdMaxLength);
        builder.Property(t => t.Notes).HasMaxLength(InvoiceTemplate.NotesMaxLength);
        builder.Property(t => t.Terms).HasMaxLength(InvoiceTemplate.TermsMaxLength);
        builder.Property(t => t.FooterNote).HasMaxLength(InvoiceTemplate.FooterMaxLength);
        builder.Property(t => t.AccentColor).IsRequired().HasMaxLength(6);
        builder.Property(t => t.ShowIssuerIdentity).IsRequired().HasDefaultValue(true);
        builder.Property(t => t.ShowLineItemTable).IsRequired().HasDefaultValue(true);
        builder.Property(t => t.ShowTerms).IsRequired().HasDefaultValue(true);
        builder.Property(t => t.ShowNotes).IsRequired().HasDefaultValue(true);
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();

        // At most one default. The partial unique index is what actually enforces it — a
        // handler-level "unset the others first" leaves a window where two rows both claim
        // the default and resolution becomes non-deterministic.
        builder.HasIndex(t => t.IsDefault)
            .IsUnique()
            .HasDatabaseName("ix_invoice_templates_single_default")
            .HasFilter("\"IsDefault\" = true");

        builder.HasIndex(t => t.Name)
            .HasDatabaseName("ix_invoice_templates_name");
    }
}

internal sealed class SupportSettingsConfiguration : IEntityTypeConfiguration<SupportSettings>
{
    public void Configure(EntityTypeBuilder<SupportSettings> builder)
    {
        builder.ToTable("support_settings", t =>
        {
            // Guards the singleton row id. Every read path writes
            //   WHERE "Id" = SupportSettings.SingletonId
            // and would silently miss a second configuration row.
            t.HasCheckConstraint(
                "ck_support_settings_singleton",
                $"\"Id\" = '{SupportSettings.SingletonId:D}'");
        });

        builder.HasKey(s => s.Id);

        builder.Property(s => s.AutoCloseIdleHours)
            .IsRequired()
            .HasDefaultValue(TicketStatusHistory.DefaultAutoCloseIdleHours);

        builder.Property(s => s.AutomatedInvoiceMailingEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.AutoGenerateInvoiceOnResolve).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.InvoiceMailSubjectOverride).HasMaxLength(200);
        builder.Property(s => s.NotifyAdminOnTicketCreated).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.NotifyAdminOnTicketReopened).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.NotifyCustomerOnTicketResolved).IsRequired().HasDefaultValue(true);
        builder.Property(s => s.MaxAttachmentsPerComment)
            .IsRequired()
            .HasDefaultValue(TicketComment.MaxAttachments);

        builder.Property(s => s.CreatedAtUtc).IsRequired();
        builder.Property(s => s.UpdatedAtUtc).IsRequired();
    }
}