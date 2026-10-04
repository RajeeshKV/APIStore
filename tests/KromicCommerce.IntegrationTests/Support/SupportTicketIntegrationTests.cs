using KromicCommerce.Application.Features.Support;
using KromicCommerce.Application.Options;
using KromicCommerce.Contracts.Support;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Support;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KromicCommerce.IntegrationTests.Support;

/// <summary>
/// Support desk behaviour against a real PostgreSQL database.
///
/// <para>
/// The guarantees under test are mostly database guarantees, which is why they cannot be
/// unit-tested: a sequence-backed reference allocator, a unique thread-path index, a partial
/// auto-close index, and CHECK constraints are all invisible to an in-memory provider. Each
/// test below names the specific failure it prevents.
/// </para>
/// </summary>
[Collection("Database")]
public sealed class SupportTicketIntegrationTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private static readonly SupportPolicyOptions Policy = new();

    private static IOptions<SupportPolicyOptions> PolicyOptions => Options.Create(Policy);

    // -----------------------------------------------------------------------
    // Reference numbering
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Concurrent_ticket_creates_receive_distinct_references()
    {
        var customer = await SeedCustomerAsync();

        // Eight contexts racing the sequence. A count-based allocator ("MAX(reference) + 1")
        // would hand several of these the same value and the losers would surface to the
        // customer as a unique-constraint 500 on the reference column.
        var references = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = Db.CreateDbContext();
            var generator = new TicketReferenceGenerator(ctx, PolicyOptions);
            return await generator.NextTicketNumberAsync();
        }));

        references.Should().OnlyHaveUniqueItems();
        references.Should().AllSatisfy(r =>
        {
            r.Should().StartWith("TKT-");
            r.Split('-').Should().HaveCount(3);
            r.Split('-')[2].Should().HaveLength(6);
        });
    }

    [SkippableFact]
    public async Task Ticket_and_invoice_references_never_collide_with_each_other()
    {
        await using var ctx = Db.CreateDbContext();
        var generator = new TicketReferenceGenerator(ctx, PolicyOptions);

        // One shared sequence, two prefixes. Separate counters per prefix would be simpler to
        // read but would produce disjoint ranges a customer could not tell apart; the shared
        // sequence keeps every reference unique regardless of which module issued it.
        var issued = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            issued.Add(await generator.NextTicketNumberAsync());
            issued.Add(await generator.NextInvoiceNumberAsync());
        }

        issued.Should().OnlyHaveUniqueItems();
        issued.Count(r => r.StartsWith("TKT-")).Should().Be(4);
        issued.Count(r => r.StartsWith("INV-")).Should().Be(4);
    }

    [SkippableFact]
    public async Task A_duplicate_ticket_reference_is_rejected_by_the_database()
    {
        var customer = await SeedCustomerAsync();
        var reference = "TKT-1999-000001";

        await using (var ctx = Db.CreateDbContext())
        {
            ctx.Tickets.Add(Ticket.Create(customer, reference, "Subject", "Description"));
            await ctx.SaveChangesAsync();
        }

        // Proves the index itself exists, so the guarantee cannot be lost by regenerating the
        // migration — a handler pre-check alone would not survive that.
        await using var verify = Db.CreateDbContext();
        var act = async () =>
        {
            verify.Tickets.Add(Ticket.Create(customer, reference, "Other", "Description"));
            await verify.SaveChangesAsync();
        };

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // -----------------------------------------------------------------------
    // Threading
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task The_thread_path_index_rejects_two_comments_claiming_the_same_position()
    {
        var customer = await SeedCustomerAsync();
        var ticket = await SeedTicketAsync(customer);

        // Inserted with raw SQL so ThreadPath is chosen here rather than derived. A second
        // comment forced onto the same path is what a lost parent-count race looks like, and
        // the unique index is the backstop that stops a rendered thread reordering itself.
        var now = DateTime.UtcNow;
        const string Path = "0000000000000000001";

        await using (var ctx = Db.CreateDbContext())
        {
            ctx.Database.ExecuteSqlRaw(
                @"INSERT INTO ticket_comments
                    (""Id"",""TicketId"",""AuthorId"",""IsAdminAuthor"",""Body"",""Depth"",
                     ""ThreadPath"",""InternalNote"",""CreatedAtUtc"",""UpdatedAtUtc"")
                  VALUES ({0},{1},{2},false,'Root',0,{3},false,{4},{4})",
                Guid.NewGuid(), ticket, customer, Path, now);
            await ctx.SaveChangesAsync();
        }

        await using var conflict = Db.CreateDbContext();
        var act = async () =>
        {
            conflict.Database.ExecuteSqlRaw(
                @"INSERT INTO ticket_comments
                    (""Id"",""TicketId"",""AuthorId"",""IsAdminAuthor"",""Body"",""Depth"",
                     ""ThreadPath"",""InternalNote"",""CreatedAtUtc"",""UpdatedAtUtc"")
                  VALUES ({0},{1},{2},false,'Impostor',0,{3},false,{4},{4})",
                Guid.NewGuid(), ticket, customer, Path, DateTime.UtcNow);
        };

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await using var verify = Db.CreateDbContext();
        (await verify.TicketComments.CountAsync(c => c.TicketId == ticket)).Should().Be(1);
    }

    [SkippableFact]
    public async Task A_replied_to_resolved_ticket_is_not_closed_out_from_under_the_customer()
    {
        var customer = await SeedCustomerAsync();
        var ticket = await SeedTicketAsync(customer);

        await using var ctx = Db.CreateDbContext();
        var entity = await ctx.Tickets.FirstAsync(t => t.Id == ticket);

        entity.Resolve(Guid.NewGuid(), "Handled", autoCloseIdleHours: 1);
        var originalDeadline = entity.AutoCloseAtUtc;
        originalDeadline.Should().NotBeNull();

        // The deadline exists to detect a silent customer. A customer who replies is not
        // silent, so the window restarts from the new message.
        entity.AddComment(customer, false, "Actually it is still broken");

        entity.AutoCloseAtUtc.Should().BeAfter(originalDeadline!.Value,
            "a customer reply must re-arm the auto-close window");

        entity.Status.Should().Be(TicketStatus.Resolved);
    }

    [SkippableFact]
    public async Task An_administrator_note_does_not_hold_the_auto_close_window_open()
    {
        var customer = await SeedCustomerAsync();
        var ticket = await SeedTicketAsync(customer);

        await using var ctx = Db.CreateDbContext();
        var entity = await ctx.Tickets.FirstAsync(t => t.Id == ticket);

        entity.Resolve(Guid.NewGuid(), "Handled", autoCloseIdleHours: 1);
        var deadline = entity.AutoCloseAtUtc;

        entity.AddComment(Guid.NewGuid(), isAdminAuthor: true, "Internal follow-up");

        entity.AutoCloseAtUtc.Should().Be(deadline,
            "the deadline counts customer silence only; an admin note 80 hours later must not " +
            "keep an abandoned conversation open");
    }

    // -----------------------------------------------------------------------
    // Lifecycle guards
    // -----------------------------------------------------------------------

    /// <summary>
    /// The "only an administrator may resolve" rule is an API-boundary guarantee, not a domain
    /// one: <c>Resolve</c> receives an actor id and cannot itself tell a customer from an admin.
    /// What the entity does guarantee is the <i>state</i> rule — a ticket that is not Open
    /// cannot be resolved again, which is what stops a double-click mailing two invoices.
    /// </summary>
    [SkippableFact]
    public void Resolving_a_ticket_that_is_not_open_is_refused()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), $"TKT-1999-{Random.Shared.Next(100000, 999999)}",
            "Subject", "Description");

        ticket.Resolve(Guid.NewGuid(), "Handled");

        var act = () => ticket.Resolve(Guid.NewGuid(), "Handled again");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be resolve*");
        ticket.ReopenCount.Should().Be(0);
    }

    [SkippableFact]
    public void An_administrator_cannot_close_a_ticket()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), $"TKT-1999-{Random.Shared.Next(100000, 999999)}",
            "Subject", "Description");
        ticket.Resolve(Guid.NewGuid(), "Handled");

        // Closure is the customer's confirmation, or the idle worker acting for them. Letting
        // an admin close it would bypass the confirmation that the fix actually worked.
        var act = () => ticket.Close(TicketTransitionActor.Admin, Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    [SkippableFact]
    public void Auto_close_is_due_only_after_the_deadline_and_only_while_resolved()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), $"TKT-1999-{Random.Shared.Next(100000, 999999)}",
            "Subject", "Description");

        ticket.IsAutoCloseDue(DateTime.UtcNow).Should().BeFalse(
            "an open ticket has no deadline and is the administrator's to work on");

        ticket.Resolve(Guid.NewGuid(), "Handled", autoCloseIdleHours: 2);
        var deadline = ticket.AutoCloseAtUtc!.Value;

        ticket.IsAutoCloseDue(deadline.AddSeconds(-1)).Should().BeFalse();
        ticket.IsAutoCloseDue(deadline).Should().BeTrue("the deadline is inclusive");
        ticket.IsAutoCloseDue(deadline.AddHours(1)).Should().BeTrue();

        ticket.Close(TicketTransitionActor.User, Guid.NewGuid());
        ticket.IsAutoCloseDue(deadline.AddDays(1)).Should().BeFalse(
            "a closed ticket has no pending deadline; reopening is a separate deliberate act");
        ticket.AutoCloseAtUtc.Should().BeNull();
    }

    [SkippableFact]
    public void Reopening_a_closed_ticket_returns_it_to_open_and_counts_the_attempt()
    {
        var ticket = Ticket.Create(Guid.NewGuid(), $"TKT-1999-{Random.Shared.Next(100000, 999999)}",
            "Subject", "Description");

        ticket.Resolve(Guid.NewGuid(), "Handled", autoCloseIdleHours: 1);
        ticket.Close(TicketTransitionActor.User, Guid.NewGuid());

        ticket.Reopen(Guid.NewGuid(), "Not fixed yet");
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.ReopenCount.Should().Be(1);
        ticket.AutoCloseAtUtc.Should().BeNull(
            "the idle clock only re-arms after the next resolution, not on reopen");

        ticket.Resolve(Guid.NewGuid(), "Handled again", autoCloseIdleHours: 1);
        ticket.Close(TicketTransitionActor.User, Guid.NewGuid());
        ticket.Reopen(Guid.NewGuid(), "Still broken");

        ticket.ReopenCount.Should().Be(2, "chronic issues are flagged by the count");
        ticket.Status.Should().Be(TicketStatus.Open);
    }

    // -----------------------------------------------------------------------
    // Seeded configuration
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task The_migration_seeds_the_support_settings_singleton_and_a_default_template()
    {
        await using var ctx = Db.CreateDbContext();

        var settings = await ctx.SupportSettings
            .SingleOrDefaultAsync(s => s.Id == SupportSettings.SingletonId);

        settings.Should().NotBeNull(
            "invoice resolution and the auto-close worker both read this row; without it the " +
            "default invoice template has nothing to fall back to");
        settings!.AutoCloseIdleHours.Should().Be(TicketStatusHistory.DefaultAutoCloseIdleHours);

        var templates = await ctx.InvoiceTemplates.Where(t => t.IsDefault).ToListAsync();
        templates.Should().ContainSingle(
            "the partial unique index allows exactly one default, and resolution needs one to exist");
    }

    [SkippableFact]
    public async Task A_second_default_invoice_template_is_rejected_by_the_database()
    {
        var now = DateTime.UtcNow;

        await using var ctx = Db.CreateDbContext();
        var act = async () =>
        {
            ctx.InvoiceTemplates.Add(InvoiceTemplate.CreateDefault("Second Default", now));
            await ctx.SaveChangesAsync();
        };

        await act.Should().ThrowAsync<DbUpdateException>(
            "the partial unique index on IsDefault is what stops template resolution becoming " +
            "non-deterministic; a handler-level 'unset the others first' leaves a window");
    }

    // -----------------------------------------------------------------------
    // Ownership
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task Another_customers_ticket_is_reported_as_not_found_not_forbidden()
    {
        var owner = await SeedCustomerAsync();
        var stranger = await SeedCustomerAsync();
        var ticket = await SeedTicketAsync(owner);

        await using var ctx = Db.CreateDbContext();
        var handler = new GetTicketQueryHandler(ctx);

        var strangerResult = await handler.Handle(
            new GetTicketQuery(ticket, stranger, IsAdmin: false), CancellationToken.None);

        strangerResult.IsFailure.Should().BeTrue();
        strangerResult.Error.Code.Should().Be("TICKET_NOT_FOUND");

        var ownerResult = await handler.Handle(
            new GetTicketQuery(ticket, owner, IsAdmin: false), CancellationToken.None);

        ownerResult.IsSuccess.Should().BeTrue("the owner must still be able to read it");
    }

    [SkippableFact]
    public async Task An_internal_note_is_invisible_to_the_customer_and_visible_to_an_administrator()
    {
        var customer = await SeedCustomerAsync();
        var admin = await SeedAdminAsync();
        var ticket = await SeedTicketAsync(customer);

        // Posted through the handler, which is the real path. This is also the regression guard
        // for the tracking bug: an internal note appended to an already-persisted ticket was
        // discovered as an existing row, so the save threw a concurrency exception.
        await using (var ctx = Db.CreateDbContext())
        {
            var handler = new AddTicketCommentHandler(
                ctx, new SupportSettingsProvider(ctx), NullLogger<AddTicketCommentHandler>.Instance);

            var note = await handler.Handle(new AddTicketCommentCommand(
                ticket, admin, IsAdmin: true, "Customer complained about this",
                ParentCommentId: null, IsInternalNote: true), CancellationToken.None);
            note.IsSuccess.Should().BeTrue();

            var reply = await handler.Handle(new AddTicketCommentCommand(
                ticket, customer, IsAdmin: false, "Any news?", ParentCommentId: null),
                CancellationToken.None);
            reply.IsSuccess.Should().BeTrue();
        }

        await using var read = Db.CreateDbContext();
        var query = new GetTicketQueryHandler(read);

        var customerView = await query.Handle(
            new GetTicketQuery(ticket, customer, IsAdmin: false), CancellationToken.None);
        customerView.IsSuccess.Should().BeTrue();

        Flatten(customerView.Value.Ticket.Comments).Select(c => c.Body)
            .Should().NotContain("Customer complained about this",
                "an internal note must be filtered by the mapper, not merely hidden by the UI");
        Flatten(customerView.Value.Ticket.Comments).Select(c => c.Body)
            .Should().Contain("Any news?");

        var adminView = await query.Handle(
            new GetTicketQuery(ticket, admin, IsAdmin: true), CancellationToken.None);
        adminView.IsSuccess.Should().BeTrue();

        Flatten(adminView.Value.Ticket.Comments).Select(c => c.Body)
            .Should().Contain("Customer complained about this");
    }

    [SkippableFact]
    public async Task An_internal_note_never_notifies_the_customer()
    {
        var customer = await SeedCustomerAsync();
        var admin = await SeedAdminAsync();
        var ticket = await SeedTicketAsync(customer);

        await using var ctx = Db.CreateDbContext();
        var handler = new AddTicketCommentHandler(
            ctx, new SupportSettingsProvider(ctx), NullLogger<AddTicketCommentHandler>.Instance);

        await handler.Handle(new AddTicketCommentCommand(
            ticket, admin, IsAdmin: true, "Internal only",
            ParentCommentId: null, IsInternalNote: true), CancellationToken.None);

        var outbox = await ctx.OutboxEvents.AsNoTracking().ToListAsync();

        outbox.Should().NotContain(e =>
                e.EventType == TicketOutbox.CommentPosted && e.Payload.Contains("Internal only"),
            "an administrative alert is addressed to the merchant; a note the customer must never " +
            "see must not be turned into a customer email");
    }

    [SkippableFact]
    public async Task A_customer_reply_to_a_closed_ticket_reopens_it_and_is_recorded_as_such()
    {
        var customer = await SeedCustomerAsync();
        var admin = await SeedAdminAsync();
        var ticket = await SeedTicketAsync(customer);

        await using (var ctx = Db.CreateDbContext())
        {
            var entity = await ctx.Tickets.FirstAsync(t => t.Id == ticket);
            ctx.TicketStatusHistory.Add(entity.Resolve(admin, "Handled", 1));
            ctx.TicketStatusHistory.Add(entity.Close(TicketTransitionActor.User, customer, "Fixed"));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = Db.CreateDbContext())
        {
            var handler = new AddTicketCommentHandler(
                ctx, new SupportSettingsProvider(ctx), NullLogger<AddTicketCommentHandler>.Instance);

            var result = await handler.Handle(new AddTicketCommentCommand(
                ticket, customer, IsAdmin: false, "It is still broken", ParentCommentId: null),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
        }

        await using var verify = Db.CreateDbContext();
        var stored = await verify.Tickets.FirstAsync(t => t.Id == ticket);

        stored.Status.Should().Be(TicketStatus.Open,
            "replying on a closed ticket is a request for help again, not a comment on history");
        stored.ReopenCount.Should().Be(1);
        stored.AutoCloseAtUtc.Should().BeNull("the idle clock re-arms only on the next resolution");

        var reopenedEntry = await verify.TicketStatusHistory
            .Where(h => h.TicketId == ticket && h.ToStatus == TicketStatus.Open)
            .OrderByDescending(h => h.OccurredAtUtc)
            .FirstOrDefaultAsync();

        reopenedEntry.Should().NotBeNull("the reopen must be in the audit log");
        reopenedEntry!.Note.Should().Contain("Customer replied on a closed ticket");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Walks the nested reply tree so an assertion can apply to every level.</summary>
    private static List<TicketCommentResponse> Flatten(IReadOnlyList<TicketCommentResponse> nodes)
    {
        var flat = new List<TicketCommentResponse>();
        foreach (var node in nodes)
        {
            flat.Add(node);
            flat.AddRange(Flatten(node.Replies));
        }

        return flat;
    }

    private async Task<Guid> SeedCustomerAsync()
    {
        await using var ctx = Db.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var user = User.CreateCustomer($"support-{suffix}@example.test", "hash", "Support", "Tester");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedAdminAsync()
    {
        await using var ctx = Db.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var user = User.CreateAdmin($"admin-{suffix}@example.test", "hash", "Admin", "Tester");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedTicketAsync(Guid customer)
    {
        await using var ctx = Db.CreateDbContext();

        // Distinct per call: the reference is uniquely indexed, so a shared constant would make
        // every test after the first collide with the first one's row.
        var reference = $"TKT-2026-{Guid.NewGuid().ToString("N")[..6]}";
        var ticket = Ticket.Create(customer, reference, "My order never arrived",
            "It has been ten days and the tracking page has not updated.");

        ctx.Tickets.Add(ticket);
        await ctx.SaveChangesAsync();
        return ticket.Id;
    }
}