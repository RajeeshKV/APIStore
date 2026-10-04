using KromicCommerce.Application.Features.Support;
using KromicCommerce.Domain.Support;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Validator-level guarantees for the support desk.
///
/// These exist because the interesting failures here are <i>silently accepting</i> ones. A
/// range check written with the wrong boolean operator does not throw and does not fail a
/// build — it accepts every input, which is the worst possible failure mode for a setting that
/// controls how long a customer conversation stays open.
/// </summary>
public sealed class SupportValidatorTests
{
    // -----------------------------------------------------------------------
    // Idle window
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(72)]
    [InlineData(720)]
    public void The_idle_window_accepts_values_inside_the_documented_range(int? hours)
    {
        var result = new UpdateSupportSettingsValidator().Validate(WithIdleWindow(hours));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(721)]
    [InlineData(int.MaxValue)]
    public void The_idle_window_rejects_values_outside_the_documented_range(int hours)
    {
        var result = new UpdateSupportSettingsValidator().Validate(WithIdleWindow(hours));

        result.IsValid.Should().BeFalse(
            "the check must be an AND of the two bounds; written as OR, '>= 1 || <= 720' is " +
            "satisfied by every integer and the setting accepts anything");
        result.Errors.Should().Contain(e => e.ErrorCode == "IdleWindowOutOfRange");
    }

    [Fact]
    public void An_unset_idle_window_is_not_a_validation_failure()
    {
        // Null means "leave as is" in the settings request, so it must not be treated as 0.
        var command = new UpdateSupportSettingsCommand(
            Guid.NewGuid(), null, null, null, null, null, null, null, null);

        new UpdateSupportSettingsValidator().Validate(command).IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Attachments
    // -----------------------------------------------------------------------

    [Fact]
    public void Only_an_administrator_may_author_an_internal_note()
    {
        var ticket = Guid.NewGuid();
        var customer = Guid.NewGuid();

        var asCustomer = new AddTicketCommentValidator().Validate(new AddTicketCommentCommand(
            ticket, customer, IsAdmin: false, "Body", null, IsInternalNote: true));

        var asAdmin = new AddTicketCommentValidator().Validate(new AddTicketCommentCommand(
            ticket, Guid.NewGuid(), IsAdmin: true, "Body", null, IsInternalNote: true));

        asCustomer.IsValid.Should().BeFalse(
            "a private note authored by a customer would be filtered out of their own view, " +
            "so they would watch their message disappear");
        asAdmin.IsValid.Should().BeTrue();
    }

    [Fact]
    public void More_attachments_than_the_ceiling_are_rejected()
    {
        var attachments = Enumerable.Range(0, TicketComment.MaxAttachments + 1)
            .Select(_ => new TicketAttachmentRequest(
                TicketAttachmentKind.Image, "public-id", "https://example.test/a.jpg",
                "jpg", "image/jpeg", 10, 10, null, 1024))
            .ToArray();

        var result = new AddTicketCommentValidator().Validate(new AddTicketCommentCommand(
            Guid.NewGuid(), Guid.NewGuid(), IsAdmin: false, "Body", null,
            IsInternalNote: false, attachments));

        result.IsValid.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Invoice accent colour
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("1A1A1A")]
    [InlineData("#1a1a1a")]
    [InlineData("FF0000")]
    [InlineData(null)]
    public void A_valid_or_absent_accent_colour_is_accepted(string? colour)
    {
        var result = new OverrideInvoiceContentValidator().Validate(new OverrideInvoiceContentCommand(
            Guid.NewGuid(), Guid.NewGuid(), null, null, "A note", null, null, colour));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("1A1A1")]
    [InlineData("1A1A1AA")]
    [InlineData("ZZZZZZ")]
    [InlineData("red")]
    public void An_invalid_accent_colour_is_rejected_rather_than_rendered_black(string colour)
    {
        var result = new OverrideInvoiceContentValidator().Validate(new OverrideInvoiceContentCommand(
            Guid.NewGuid(), Guid.NewGuid(), null, null, "A note", null, null, colour));

        result.IsValid.Should().BeFalse(
            "a malformed colour silently renders as a black document, which reaches the customer");
    }

    [Fact]
    public void An_override_that_changes_nothing_is_rejected()
    {
        var result = new OverrideInvoiceContentValidator().Validate(new OverrideInvoiceContentCommand(
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, null, null, null));

        result.IsValid.Should().BeFalse(
            "it would flag the revision as customised and record a misleading audit trail");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static UpdateSupportSettingsCommand WithIdleWindow(int? hours) =>
        new(Guid.NewGuid(), null, null, null, hours, null, null, null, null);
}