using KromicCommerce.Application.Features.Store.UpdateEmailSettings;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Conditional email settings validation.
///
/// The two email modes have genuinely different requirements, and the previous validation
/// accepted input the domain then rejected with an ArgumentException — which surfaced as an
/// HTTP 500 rather than a validation error. These tests pin the mode-conditional contract at
/// both layers so the two cannot drift apart again.
/// </summary>
public sealed class EmailSettingsValidationTests
{
    // -----------------------------------------------------------------------
    // KromicManaged — sender address is irrelevant
    // -----------------------------------------------------------------------

    [Fact]
    public void KromicManaged_accepts_a_request_with_no_sender_email()
    {
        var result = Validator().Validate(
            new UpdateEmailSettingsCommand(EmailMode.KromicManaged, "My Store", null));

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// In this mode the store has no provider credentials and emails go out from Kromic's own
    /// account, so the sender address plays no part. Requiring it would block a store that has
    /// configured this mode correctly.
    /// </summary>
    [Fact]
    public void KromicManaged_does_not_require_a_sender_email()
    {
        var act = () => EmailSettings.Create(EmailMode.KromicManaged, "My Store", null);

        act.Should().NotThrow();
        act().SenderEmail.Should().BeNull();
    }

    /// <summary>
    /// A supplied address is irrelevant in this mode, and silently discarding it would leave an
    /// admin UI showing "saved" for a field that was never stored. Rejecting it tells the UI to
    /// stop sending the field.
    /// </summary>
    [Theory]
    [InlineData("store@example.com")]
    [InlineData("not-an-email")]
    public void KromicManaged_rejects_a_supplied_sender_email(string senderEmail)
    {
        var result = Validator().Validate(
            new UpdateEmailSettingsCommand(EmailMode.KromicManaged, "My Store", senderEmail));

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// A blank or whitespace-only value is treated as "not supplied" and accepted, so a form
    /// that posts an empty string is not rejected for a field the mode ignores.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void KromicManaged_treats_a_blank_sender_email_as_omitted(string senderEmail)
    {
        var result = Validator().Validate(
            new UpdateEmailSettingsCommand(EmailMode.KromicManaged, "My Store", senderEmail));

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Switching modes and back must not resurrect a stale address from the previous mode.
    /// </summary>
    [Fact]
    public void KromicManaged_stores_no_sender_email_even_when_one_is_supplied()
    {
        var settings = EmailSettings.Create(EmailMode.KromicManaged, "My Store", "leftover@example.com");

        settings.SenderEmail.Should().BeNull();
        settings.Mode.Should().Be(EmailMode.KromicManaged);
    }

    // -----------------------------------------------------------------------
    // CustomerBrevo — sender address is mandatory and must be valid
    // -----------------------------------------------------------------------

    [Fact]
    public void CustomerBrevo_accepts_a_valid_sender_email()
    {
        var result = Validator().Validate(
            new UpdateEmailSettingsCommand(EmailMode.CustomerBrevo, "My Store", "store@example.com"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CustomerBrevo_requires_a_sender_email(string? senderEmail)
    {
        var result = Validator().Validate(
            new UpdateEmailSettingsCommand(EmailMode.CustomerBrevo, "My Store", senderEmail));

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// This is the case that used to reach the domain and throw: the validator's NotEmpty()
    /// accepted a whitespace-only value while the domain used IsNullOrWhiteSpace, producing an
    /// unhandled ArgumentException and a 500.
    /// </summary>
    [Fact]
    public void CustomerBrevo_rejects_a_whitespace_only_sender_email_without_throwing()
    {
        var result = Validator().Validate(
            new UpdateEmailSettingsCommand(EmailMode.CustomerBrevo, "My Store", "   "));

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("store")]
    [InlineData("store@")]
    [InlineData("@example.com")]
    [InlineData("store@example")]
    [InlineData("store@.com")]
    [InlineData("store@example.")]
    public void CustomerBrevo_rejects_a_malformed_sender_email(string senderEmail)
    {
        var result = Validator().Validate(
            new UpdateEmailSettingsCommand(EmailMode.CustomerBrevo, "My Store", senderEmail));

        result.IsValid.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Always-applicable rules
    // -----------------------------------------------------------------------

    /// <summary>
    /// The sender name labels the "From" line in both modes, so it is required regardless of
    /// which mode is selected.
    /// </summary>
    [Theory]
    [InlineData(EmailMode.KromicManaged)]
    [InlineData(EmailMode.CustomerBrevo)]
    public void The_sender_name_is_required_in_both_modes(EmailMode mode)
    {
        var validator = Validator();

        validator.Validate(new UpdateEmailSettingsCommand(mode, "", null)).IsValid.Should().BeFalse();
        validator.Validate(new UpdateEmailSettingsCommand(mode, "   ", null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void The_sender_name_is_trimmed_by_the_domain()
    {
        EmailSettings.Create(EmailMode.KromicManaged, "  My Store  ", null)
            .SenderName.Should().Be("My Store");
    }

    [Fact]
    public void An_out_of_range_mode_is_rejected()
    {
        var result = Validator().Validate(
            new UpdateEmailSettingsCommand((EmailMode)99, "My Store", null));

        result.IsValid.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Validator and domain agree
    // -----------------------------------------------------------------------

    /// <summary>
    /// The whole point: every case the domain rejects must be rejected by the validator FIRST,
    /// so the handler's ArgumentException guard is only defence in depth rather than the normal
    /// path to a 500. If the two ever disagree, this test fails.
    /// </summary>
    [Theory]
    [InlineData(EmailMode.KromicManaged, "", "")]
    [InlineData(EmailMode.KromicManaged, "   ", null)]
    [InlineData(EmailMode.KromicManaged, "My Store", "")]
    [InlineData(EmailMode.CustomerBrevo, "My Store", null)]
    [InlineData(EmailMode.CustomerBrevo, "My Store", "")]
    [InlineData(EmailMode.CustomerBrevo, "My Store", "   ")]
    public void Every_case_the_domain_would_reject_is_rejected_by_the_validator_first(
        EmailMode mode, string senderName, string? senderEmail)
    {
        var command = new UpdateEmailSettingsCommand(mode, senderName, senderEmail);

        var domainRejects = ThrowsArgumentException(() => EmailSettings.Create(mode, senderName, senderEmail));

        if (domainRejects)
            Validator().Validate(command).IsValid.Should().BeFalse(
                "the validator must reject this before the domain throws, or the request returns 500");
    }

    private static bool ThrowsArgumentException(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }

    private static UpdateEmailSettingsValidator Validator() => new();
}
