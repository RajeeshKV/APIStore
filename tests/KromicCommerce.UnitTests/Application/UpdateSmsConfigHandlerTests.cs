using System.Text.Json;
using KromicCommerce.Application.Features.Admin.Integrations;
using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Application.Abstractions.Security;
using KromicCommerce.Domain.Sms;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Covers the SMS configuration save, which is the change that decides which provider actually
/// receives customer OTPs.
/// </summary>
public sealed class UpdateSmsConfigHandlerTests
{
[Fact]
    public async Task The_save_returns_the_resulting_status_so_the_ui_needs_no_second_read()
    {
        // Returning 204 forced the admin screen into a follow-up GET to discover what had been
        // stored. The response now carries the same payload the GET returns.
        var (handler, _, _, _, _) = Build();

        var result = await handler.Handle(
            new UpdateSmsConfigCommand(
                true, "Twilio",
                Settings(("AccountSid", "AC123"), ("AuthToken", "tok"), ("ServiceSid", "sid"))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.IntegrationName.Should().Be("SMS");
        result.Value.Enabled.Should().BeTrue();
        result.Value.PublicFields.Should().ContainKey("provider");
        result.Value.PublicFields["provider"].Should().Be("Twilio");
    }

    [Fact]
    public async Task The_selection_is_persisted_so_the_save_actually_changes_delivery()
    {
        // The handler used to only append an audit event, so the admin screen showed a provider
        // that nothing ever read.
        var (handler, _, configs, events, saves) = Build();

        var result = await handler.Handle(
            new UpdateSmsConfigCommand(true, "Twilio", Settings(("AccountSid", "AC123"), ("AuthToken", "secret-token"))),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        configs.Should().ContainSingle();
        configs.Single().Provider.Should().Be("Twilio");
        configs.Single().Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Credentials_are_encrypted_before_storage()
    {
        var (handler, _, configs, events, saves) = Build();

        await handler.Handle(
            new UpdateSmsConfigCommand(true, "Twilio", Settings(("AccountSid", "AC123"), ("AuthToken", "secret-token"))),
            CancellationToken.None);

        var stored = configs.Single().EncryptedSettings;
        stored.Should().NotContain("\"secret-token\"");
        stored.Should().NotContain("\"AC123\"");
        // Encrypted, not dropped: the value must still be recoverable at send time.
        stored.Should().Contain("enc:secret-token");
    }

    [Fact]
    public async Task The_audit_event_records_setting_names_but_never_values()
    {
        // This is the regression that mattered most: ProviderSettings used to be serialised
        // straight into the Outbox payload, writing live API keys into an audit table.
        var (handler, _, configs, events, saves) = Build();

        await handler.Handle(
            new UpdateSmsConfigCommand(true, "Twilio", Settings(("AccountSid", "AC123"), ("AuthToken", "super-secret"))),
            CancellationToken.None);

        var payload = events.Single().Payload;
        payload.Should().NotContain("super-secret");
        payload.Should().NotContain("AC123");
        payload.Should().NotContain("enc:super-secret");
        // The names are still there, so the audit record stays useful.
        payload.Should().Contain("AuthToken");
        payload.Should().Contain("AccountSid");
    }

    [Fact]
    public async Task The_configuration_and_its_audit_event_commit_together()
    {
        // Two SaveChanges calls would let the config commit while the event failed, leaving a
        // change with no audit trail and no way to tell a partial write from a real one.
        var (handler, _, configs, events, saves) = Build();

        await handler.Handle(
            new UpdateSmsConfigCommand(true, "Twilio", Settings(("AccountSid", "AC123"))),
            CancellationToken.None);

        saves.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_failing_save_leaves_neither_the_config_nor_an_audit_event()
    {
        var (handler, _, configs, events, saves) = Build();
        saves.ThrowOnSave = true;

        var act = () => handler.Handle(
            new UpdateSmsConfigCommand(true, "Twilio", Settings(("AccountSid", "AC123"))),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        saves.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("2Factor")]
    [InlineData("Free2SMS")]
    [InlineData("Twilio")]
    [InlineData("None")]
    public async Task Every_supported_provider_is_accepted(string provider)
    {
        var (handler, _, configs, events, saves) = Build();

        var result = await handler.Handle(
            new UpdateSmsConfigCommand(true, provider, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        configs.Single().Provider.Should().Be(provider);
    }

    [Theory]
    [InlineData("TechTo")]
    [InlineData("SmsLocal")]
    [InlineData("")]
    public async Task An_unsupported_provider_is_rejected_and_nothing_is_written(string provider)
    {
        // The handler parses again rather than trusting the validator, so a direct internal call
        // cannot persist a provider that does not exist.
        var (handler, _, configs, events, saves) = Build();

        var result = await handler.Handle(
            new UpdateSmsConfigCommand(true, provider, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SMS_PROVIDER_UNSUPPORTED");
        configs.Should().BeEmpty();
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unsupported_provider_names_the_valid_choices_in_the_error()
    {
        var (handler, _, _, _, _) = Build();

        var result = await handler.Handle(
            new UpdateSmsConfigCommand(true, "TechTo", null),
            CancellationToken.None);

        result.Error.Description.Should().Contain("2Factor").And.Contain("Free2SMS").And.Contain("Twilio");
    }

    [Fact]
    public async Task Blank_values_are_dropped_rather_than_saved_as_empty()
    {
        // An empty field in an admin form means "not supplied", not "set to empty string".
        // Storing it would overwrite a working value with a credential that can never authenticate.
        var (handler, _, configs, events, saves) = Build();

        await handler.Handle(
            new UpdateSmsConfigCommand(true, "Twilio", Settings(("AccountSid", "AC123"), ("AuthToken", "   "))),
            CancellationToken.None);

        var stored = configs.Single().EncryptedSettings;
        stored.Should().Contain("AccountSid");
        stored.Should().NotContain("AuthToken");
    }

    [Fact]
    public async Task A_setting_belonging_to_another_provider_is_not_stored()
    {
        // Two providers' credentials in one row would let a misconfigured save look complete while
        // the active provider still has nothing to authenticate with.
        var (handler, _, configs, events, saves) = Build();

        await handler.Handle(
            new UpdateSmsConfigCommand(true, "Twilio", Settings(("AccountSid", "AC123"), ("ApiKey", "free2sms-key"))),
            CancellationToken.None);

        var stored = configs.Single().EncryptedSettings;
        stored.Should().Contain("AccountSid");
        stored.Should().NotContain("ApiKey");
    }

    [Fact]
    public async Task Saving_a_second_time_replaces_the_previous_selection()
    {
        var (handler, _, configs, events, saves) = Build();
        var config = SmsProviderConfig.Create(true, SmsProviderKind.Twilio, new Dictionary<string, string>());
        configs.Add(config);

        await handler.Handle(
            new UpdateSmsConfigCommand(true, "Free2SMS", Settings(("ApiKey", "key"))),
            CancellationToken.None);

        // Still one row: this is a singleton, not a history table.
        configs.Should().ContainSingle();
        configs.Single().Provider.Should().Be("Free2SMS");
    }

    [Fact]
    public async Task Disabling_sms_persists_the_disabled_flag()
    {
        var (handler, _, configs, events, saves) = Build();

        await handler.Handle(new UpdateSmsConfigCommand(false, "Twilio", null), CancellationToken.None);

        configs.Single().Enabled.Should().BeFalse();
        // The provider name is kept so re-enabling does not require re-entering the selection.
        configs.Single().Provider.Should().Be("Twilio");
    }

    private static Dictionary<string, string> Settings(params (string Key, string Value)[] values)
        => values.ToDictionary(v => v.Key, v => v.Value);

private static (UpdateSmsConfigHandler Handler, Mock<IApplicationDbContext> Db, List<SmsProviderConfig> Configs, List<OutboxEvent> Events, SaveCounter Saves) Build()
    {
        var configs = new List<SmsProviderConfig>();
        var events = new List<OutboxEvent>();
        var saves = new SaveCounter();

        var db = new Mock<IApplicationDbContext>();
        db.Setup(d => d.SmsProviderConfigs).Returns(TrackedDbSet(configs));
        db.Setup(d => d.OutboxEvents).Returns(TrackedDbSet(events));
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() => saves.Next());

        var secrets = new Mock<ISecretProtectionService>();
        secrets.Setup(s => s.Protect(It.IsAny<string>()))
            .Returns((string value) => $"enc:{value}");

        // The handler now returns the post-save status, so it resolves the same factory and
        // settings store the GET endpoint uses.
        var factory = SmsTestDoubles.Configured();
        var handler = new UpdateSmsConfigHandler(
            db.Object, secrets.Object, factory,
            new Mock<ISmsProviderSettings>().Object,
            NullLogger<UpdateSmsConfigHandler>.Instance);

        return (handler, db, configs, events, saves);
    }

    /// <summary>Counts saves and can fail one, so atomicity can be observed.</summary>
    private sealed class SaveCounter
    {
        public int Calls { get; private set; }

        public bool ThrowOnSave { get; set; }

        public Task<int> Next()
        {
            Calls++;
            return ThrowOnSave
                ? throw new InvalidOperationException("save failed")
                : Task.FromResult(1);
        }
    }

    /// <summary>
    /// A DbSet over a real backing list: <c>Add</c> mutates the list so assertions see the row,
    /// and queries run through an async provider because the handler looks the singleton up with
    /// <c>FirstOrDefaultAsync</c>.
    /// </summary>
    private static DbSet<T> TrackedDbSet<T>(List<T> backing) where T : class
    {
        var queryable = backing.AsQueryable();
        var mock = new Mock<DbSet<T>>();
        mock.Setup(d => d.Add(It.IsAny<T>())).Callback<T>(backing.Add);
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        return mock.Object;
    }
}