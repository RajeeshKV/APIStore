using KromicCommerce.Application.Features.Support;
using KromicCommerce.Contracts.Support;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Support;
using KromicCommerce.IntegrationTests.Infrastructure;

namespace KromicCommerce.IntegrationTests.Support;

/// <summary>
/// The admin ticket list and the customer ticket list, against a real PostgreSQL database.
/// </summary>
/// <remarks>
/// <para>
/// These tests exist because of a specific production failure: both handlers order by
/// <c>LastActivityAtUtc</c> on <see cref="TicketSummaryProjection"/>, whose body carries two
/// correlated subqueries (comment count, latest invoice status). PostgreSQL cannot translate that
/// ORDER BY, so every request failed with a 500 and the admin support queue was unusable.
/// </para>
/// <para>
/// An in-memory provider would have passed regardless, because it does not translate to SQL at
/// all. Only a relational provider catches this class of bug, which is why the suite uses a real
/// PostgreSQL container and why the assertion is about the query completing rather than about any
/// particular ordering of a deliberately tiny data set.
/// </para>
/// </remarks>
[Collection("Database")]
public sealed class AdminTicketListIntegrationTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    [SkippableFact]
    public async Task The_admin_ticket_list_returns_a_page_instead_of_failing_to_translate()
    {
        var customer = await SeedCustomerAsync();
        var first = await SeedTicketAsync(customer);
        var second = await SeedTicketAsync(customer);

        await using var ctx = Db.CreateDbContext();
        var handler = new GetAdminTicketsQueryHandler(ctx);

        var result = await handler.Handle(
            new GetAdminTicketsQuery(null, null, null, null, 1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the ORDER BY must be translatable by PostgreSQL");

        var page = result.Value;
        page.TotalCount.Should().BeGreaterThanOrEqualTo(2);
        page.Items.Select(i => i.Id).Should().Contain([first, second]);
    }

    [SkippableFact]
    public async Task The_admin_ticket_list_orders_by_most_recently_active_first()
    {
        var customer = await SeedCustomerAsync();

        var older = await SeedTicketAsync(customer);
        var newer = await SeedTicketAsync(customer);

        // Pin the ordering key rather than relying on creation order: two tickets created in the
        // same test can share a timestamp, and an assertion that passed by luck would not catch
        // a regression in the ORDER BY clause itself.
        await using (var ctx = Db.CreateDbContext())
        {
            var ticket = await ctx.Tickets.FirstAsync(t => t.Id == older);
            ticket.SetPriority(TicketPriority.High, DateTime.UtcNow.AddMinutes(5));
            await ctx.SaveChangesAsync();
        }

        await using var listCtx = Db.CreateDbContext();
        var handler = new GetAdminTicketsQueryHandler(listCtx);

        var result = await handler.Handle(
            new GetAdminTicketsQuery(null, null, null, null, 1, 50), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var items = result.Value.Items.Where(i => i.Id is var id && (id == older || id == newer)).ToList();
        items.Should().HaveCount(2);
        items[0].Id.Should().Be(older, "the ticket touched most recently belongs at the top");
        items[1].Id.Should().Be(newer);
    }

    [SkippableFact]
    public async Task The_admin_ticket_list_filters_by_status_and_search()
    {
        var customer = await SeedCustomerAsync();
        var target = await SeedTicketAsync(customer);

        await using var ctx = Db.CreateDbContext();
        var handler = new GetAdminTicketsQueryHandler(ctx);

        // Filters ride the same projection as the ordering, so each one is exercised here to
        // catch a filter that survives ordering but breaks on its own.
        var byStatus = await handler.Handle(
            new GetAdminTicketsQuery(TicketStatus.Open, null, null, null, 1, 50), CancellationToken.None);
        byStatus.IsSuccess.Should().BeTrue();
        byStatus.Value.Items.Should().Contain(i => i.Id == target);

        var bySearch = await handler.Handle(
            new GetAdminTicketsQuery(null, null, "order never arrived", null, 1, 50), CancellationToken.None);
        bySearch.IsSuccess.Should().BeTrue();
        bySearch.Value.Items.Should().Contain(i => i.Id == target);
    }

    [SkippableFact]
    public async Task The_customer_ticket_list_returns_a_page_instead_of_failing_to_translate()
    {
        var customer = await SeedCustomerAsync();
        var ticket = await SeedTicketAsync(customer);

        await using var ctx = Db.CreateDbContext();
        var handler = new GetMyTicketsQueryHandler(ctx);

        var result = await handler.Handle(
            new GetMyTicketsQuery(customer, null, 1, 20), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the ORDER BY must be translatable by PostgreSQL");
        result.Value.Items.Should().Contain(i => i.Id == ticket);
    }

    private async Task<Guid> SeedCustomerAsync()
    {
        await using var ctx = Db.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];

        var user = User.CreateCustomer($"adminlist-{suffix}@example.test", "hash", "Support", "Tester");
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Guid> SeedTicketAsync(Guid customer)
    {
        await using var ctx = Db.CreateDbContext();

        // Unique per call: the reference is uniquely indexed, so a shared constant would collide
        // with any earlier test that already used it.
        var reference = $"TKT-2026-{Guid.NewGuid().ToString("N")[..6]}";
        var ticket = Ticket.Create(customer, reference, "My order never arrived",
            "It has been ten days and the tracking page has not updated.");

        ctx.Tickets.Add(ticket);
        await ctx.SaveChangesAsync();
        return ticket.Id;
    }
}
