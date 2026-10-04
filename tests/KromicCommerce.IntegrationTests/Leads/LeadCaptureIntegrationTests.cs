using KromicCommerce.Application.Features.Leads;
using KromicCommerce.Application.Options;
using KromicCommerce.Domain.Leads;
using KromicCommerce.Domain.Outbox;
using KromicCommerce.Infrastructure.Persistence;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KromicCommerce.IntegrationTests.Leads;

/// <summary>
/// Lead capture against a real PostgreSQL database.
///
/// <para>
/// The guarantees here are mostly the ones that only bite in production: that a public endpoint
/// cannot be turned into a mail relay, that a bot cannot use it to flood a real inbox, and that
/// a double-tap does not surface a database error to a visitor who did nothing wrong.
/// </para>
/// </summary>
[Collection("Database")]
public sealed class LeadCaptureIntegrationTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private static CreateLeadHandler Handler(AppDbContext ctx, string? recipient) =>
        new(ctx, Options.Create(new LeadPolicyOptions
        {
            NotificationEmail = recipient,
            NotificationName = "Website Enquiries"
        }), NullLogger<CreateLeadHandler>.Instance);

    private static CreateLeadCommand Cmd(
        string? name = "Rahul Sharma",
        string? phone = "+91 98765 43210",
        string? email = "rahul@example.test",
        string? business = "Kromic Retail Pvt Ltd",
        string? source = "hero-form",
        string? honeypot = null) =>
        new(
            name ?? string.Empty,
            phone ?? string.Empty,
            email ?? string.Empty,
            business ?? string.Empty,
            source,
            honeypot,
            IpAddress: "203.0.113.9",
            UserAgent: "TestAgent/1.0");

    // -----------------------------------------------------------------------
    // Capture
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task A_submission_is_stored_and_queues_exactly_one_notification()
    {
        await using var ctx = Db.CreateDbContext();
        var handler = Handler(ctx, "hello@shopey.tech");
        var phone = NewPhone();

        var result = await handler.Handle(Cmd(phone: phone), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var stored = await ctx.Leads.AsNoTracking().SingleAsync(l => l.Phone == phone);
        stored.Name.Should().Be("Rahul Sharma");
        stored.Email.Should().Be("rahul@example.test");
        stored.Business.Should().Be("Kromic Retail Pvt Ltd");
        stored.Status.Should().Be(LeadStatus.New);
        stored.Source.Should().Be("hero-form");
        stored.NotifiedAt.Should().BeFalse("the outbox worker sends it, not the request");

        // Scoped to this lead, not the whole table: the fixture shares one database across the
        // suite, so an unscoped count measures every other test that ran first.
        var queued = await EventsForAsync(ctx, stored.Id);

        queued.Should().HaveCount(1, "one notification per enquiry, not one per save");
    }

    [SkippableFact]
    public async Task The_phone_is_stored_normalised_while_the_typed_form_is_kept_for_display()
    {
        await using var ctx = Db.CreateDbContext();
        var handler = Handler(ctx, "hello@shopey.tech");

        var digits = $"{Random.Shared.Next(100_000_000, 999_999_999)}";
        var typed = $"+91 {digits[..3]} {digits[3..6]} {digits[6..]}";

        await handler.Handle(Cmd(phone: typed), CancellationToken.None);

        var stored = await ctx.Leads.AsNoTracking().SingleAsync(l => l.PhoneRaw == typed);

        stored.Phone.Should().Be($"+91{digits}",
            "'+91 98765 43210' and '+919876543210' are one number typed two ways, and a sales " +
            "team searching by number should not have to try every spelling");
    }

    [Fact]
    public void Phone_normalisation_is_lossless_and_never_invents_a_country_code()
    {
        // Prepending a guessed code would produce a number that dials a different person, which
        // is not a recoverable error for a sales call.
        Lead.NormalisePhone("9876543210").Should().Be("9876543210");
        Lead.NormalisePhone("+91 98765 43210").Should().Be("+919876543210");
        Lead.NormalisePhone("(020) 7946-0958").Should().Be("02079460958");
    }

    // -----------------------------------------------------------------------
    // The endpoint must not be a mail relay
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task The_notification_destination_comes_from_configuration_not_the_submission()
    {
        await using var ctx = Db.CreateDbContext();
        var phone = NewPhone();

        // The visitor's address is attacker-controlled; the destination is not.
        await Handler(ctx, "hello@shopey.tech").Handle(
            Cmd(phone: phone, email: "attacker@evil.test"), CancellationToken.None);

        var evt = (await EventsForAsync(ctx, payloadLeadId: await LeadIdForPhoneAsync(ctx, phone)))
            .Single();

        var payload = System.Text.Json.JsonSerializer.Deserialize<LeadSubmittedPayload>(evt.Payload)!;

        payload.Email.Should().Be("attacker@evil.test",
            "the submitted address is carried so the dispatcher can set Reply-To");
        evt.Payload.Should().NotContain("hello@shopey.tech",
            "the destination is resolved by the dispatcher from configuration; writing it into the " +
            "payload would let a request decide where operational mail goes");
    }

    [SkippableFact]
    public async Task A_lead_is_still_stored_when_no_notification_address_is_configured()
    {
        await using var ctx = Db.CreateDbContext();
        var phone = NewPhone();

        var result = await Handler(ctx, recipient: null)
            .Handle(Cmd(phone: phone), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await ctx.Leads.AsNoTracking().AnyAsync(l => l.Phone == phone)).Should().BeTrue(
            "losing a lead because a deployment forgot an environment variable would be far " +
            "worse than a late notification");
    }

    // -----------------------------------------------------------------------
    // Abuse resistance
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task A_filled_honeypot_is_accepted_but_stores_nothing_and_queues_no_email()
    {
        await using var ctx = Db.CreateDbContext();
        var phone = NewPhone();

        var result = await Handler(ctx, "hello@shopey.tech")
            .Handle(Cmd(phone: phone, honeypot: "http://spam.example"), CancellationToken.None);

        // Same success a bot would get from a genuine submission. A distinguishable status tells
        // it exactly which field gave it away, and it simply stops filling that one.
        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();

        (await ctx.Leads.AsNoTracking().AnyAsync(l => l.Phone == phone)).Should().BeFalse();
        (await EventsForAsync(ctx, phone: phone)).Should().BeEmpty(
            "storing the bot's details would only hand it a free data sink");
    }

    [SkippableFact]
    public async Task A_second_submission_for_the_same_number_is_a_conflict_not_a_crash()
    {
        await using var ctx = Db.CreateDbContext();
        var handler = Handler(ctx, "hello@shopey.tech");
        var phone = NewPhone();

        (await handler.Handle(Cmd(phone: phone), CancellationToken.None)).IsSuccess.Should().BeTrue();

        var second = await handler.Handle(
            Cmd(phone: phone, business: "Different Business Name"), CancellationToken.None);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("LEAD_ALREADY_CAPTURED");

        // The first submission is untouched, so nothing the visitor typed is lost or overwritten.
        var stored = await ctx.Leads.AsNoTracking().SingleAsync(l => l.Phone == phone);
        stored.Business.Should().Be("Kromic Retail Pvt Ltd");
    }

    [SkippableFact]
    public async Task The_index_rejects_a_duplicate_number_even_when_the_handler_is_bypassed()
    {
        var phone = NewPhone();

        await using (var ctx = Db.CreateDbContext())
        {
            ctx.Leads.Add(Lead.Create("Rahul Sharma", phone, "rahul@example.test", "Retail"));
            await ctx.SaveChangesAsync();
        }

        // The handler translates the violation into a friendly 409, but only the index itself
        // guarantees it. A handler pre-check alone would not survive a second writer.
        await using var ctx2 = Db.CreateDbContext();
        var act = async () =>
        {
            ctx2.Leads.Add(Lead.Create("Someone Else", phone, "x@example.test", "Retail"));
            await ctx2.SaveChangesAsync();
        };

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SkippableFact]
    public async Task An_archived_lead_frees_its_number_for_a_genuine_second_enquiry()
    {
        var phone = NewPhone();

        await using (var ctx = Db.CreateDbContext())
        {
            var archived = Lead.Create("Rahul Sharma", phone, "rahul@example.test", "Retail");
            archived.Archive();
            ctx.Leads.Add(archived);
            await ctx.SaveChangesAsync();
        }

        // The unique index is partial on IsArchived, so someone who enquires again months later
        // can be recorded. A blanket unique constraint would permanently block them.
        await using var ctx2 = Db.CreateDbContext();
        var act = async () =>
        {
            ctx2.Leads.Add(Lead.Create("Rahul Sharma", phone, "rahul@example.test", "Retail"));
            await ctx2.SaveChangesAsync();
        };

        await act.Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("12345")]        // too few digits to dial
    [InlineData("+91 1 2 3")]    // too few once formatting is stripped
    [InlineData("")]
    public void A_phone_that_cannot_be_dialled_is_rejected(string phone)
    {
        var act = () => Lead.NormalisePhone(phone);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Validation_rejects_the_values_a_form_would_produce_by_mistake()
    {
        var v = new CreateLeadValidator();

        v.Validate(Cmd()).IsValid.Should().BeTrue();

        v.Validate(Cmd(phone: "12345")).IsValid.Should().BeFalse();
        v.Validate(Cmd(email: "not-an-email")).IsValid.Should().BeFalse();
        v.Validate(Cmd(name: "R")).IsValid.Should().BeFalse();
        v.Validate(Cmd(business: "x")).IsValid.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Admin list
    // -----------------------------------------------------------------------

    [SkippableFact]
    public async Task The_admin_list_hides_archived_leads_unless_asked_for_them()
    {
        await using (var ctx = Db.CreateDbContext())
        {
            var archived = Lead.Create("Archived Lead", NewPhone(), "old@example.test", "Retail");
            archived.Archive();
            ctx.Leads.AddRange(
                Lead.Create("Active Lead", NewPhone(), "active@example.test", "Retail"),
                archived);
            await ctx.SaveChangesAsync();
        }

        await using var read = Db.CreateDbContext();
        var handler = new GetLeadsHandler(read);

        var visible = await handler.Handle(
            new GetLeadsQuery(null, null, IncludeArchived: false, 1, 200), CancellationToken.None);
        var all = await handler.Handle(
            new GetLeadsQuery(null, null, IncludeArchived: true, 1, 200), CancellationToken.None);

        visible.IsSuccess.Should().BeTrue();
        all.IsSuccess.Should().BeTrue();
        visible.Value.TotalCount.Should().BeLessThan(all.Value.TotalCount);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// A unique dialled number per call. The phone column is uniquely indexed across active rows,
    /// so a shared constant would collide with any earlier test that used it.
    /// </summary>
    private static string NewPhone() => $"+91{Random.Shared.Next(100_000_000, 999_999_999)}";

    private static Task<Guid> LeadIdForPhoneAsync(AppDbContext ctx, string phone) =>
        ctx.Leads.AsNoTracking().Where(l => l.Phone == phone).Select(l => l.Id).SingleAsync();

    /// <summary>
    /// Notification events belonging to one lead.
    ///
    /// The fixture shares a single database across the suite, so an unscoped query over
    /// <c>outbox_events</c> measures every lead any earlier test created. Scoping by the id
    /// embedded in the payload keeps each assertion about its own submission.
    /// </summary>
    private static Task<List<OutboxEvent>> EventsForAsync(
        AppDbContext ctx, Guid? payloadLeadId = null, string? phone = null)
    {
        var q = ctx.OutboxEvents.AsNoTracking()
            .Where(e => e.EventType == LeadOutbox.Submitted);

        if (payloadLeadId is not null)
            q = q.Where(e => e.Payload.Contains(payloadLeadId.Value.ToString()));

        if (phone is not null)
            q = q.Where(e => e.Payload.Contains(phone));

        return q.ToListAsync();
    }
}