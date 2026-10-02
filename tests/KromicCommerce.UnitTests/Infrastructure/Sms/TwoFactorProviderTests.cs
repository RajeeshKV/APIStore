using System.Net;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

public sealed class TwoFactorProviderTests
{
    private static readonly SmsOtpPolicyOptions Policy = new();

    private static readonly TwoFactorOptions Complete = new() { ApiKey = "2fa-secret" };

    private const string Accepted = """{"Status":"Success","SessionId":"SESS-1"}""";

    /// <summary>An active template whose vendor reference is the 2Factor template <b>name</b>.</summary>
    private static ISmsTemplateStore NamedTemplate(string reference = "LOGIN_OTP", string body = "")
        => SmsTestDoubles.BodyTemplate(SmsProviderKind.TwoFactor, body, reference);

    private static (TwoFactorProvider Provider, RecordingHttpMessageHandler Handler, RecordingAuditSink Audit) Build(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = Accepted,
        TwoFactorOptions? options = null,
        ISmsTemplateStore? templates = null,
        SmsProviderSettingsSnapshot? saved = null,
        Action<RecordingHttpMessageHandler>? configure = null)
    {
        var handler = new RecordingHttpMessageHandler(status, body);
        configure?.Invoke(handler);
        var audit = new RecordingAuditSink();

        var provider = new TwoFactorProvider(
            Options.Create(options ?? Complete),
            Options.Create(Policy),
            saved,
            templates ?? NamedTemplate(),
            audit,
            new StubHttpClientFactory(handler),
            NullLogger<TwoFactorProvider>.Instance);

        return (provider, handler, audit);
    }

    // ---------------------------------------------------------------- native OTP route

    [Fact]
    public async Task Uses_the_providers_native_otp_route_by_default()
    {
        var (provider, handler, _) = Build();

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        result.ProviderMessageId.Should().Be("SESS-1");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be("https://2factor.in/API/V1/OTP/SEND");
    }

    [Fact]
    public async Task Sends_the_api_key_in_the_configured_header_and_not_in_the_body()
    {
        var (provider, handler, _) = Build();

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Headers["X-API-Key"].Should().Be("2fa-secret");
        handler.Requests[0].Body.Should().NotContain("2fa-secret");
    }

    [Fact]
    public async Task Sends_the_api_key_in_the_body_when_no_header_is_configured()
    {
        // Some 2Factor account generations authenticate with `apiKey` in the body instead.
        var (provider, handler, _) = Build(
            options: new TwoFactorOptions { ApiKey = Complete.ApiKey, ApiKeyHeader = "" });

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Headers.ContainsKey("X-API-Key").Should().BeFalse();
        handler.Requests[0].Body.Should().Contain("\"apiKey\":\"2fa-secret\"");
    }

    [Fact]
    public async Task Sends_the_administrators_template_name_under_the_configured_field()
    {
        var (provider, handler, _) = Build(templates: NamedTemplate("LOGIN_OTP"));

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Body.Should().Contain("\"template_name\":\"LOGIN_OTP\"");
        handler.Requests[0].Body.Should().Contain("\"var1\":\"4829\"");
        handler.Requests[0].Body.Should().Contain("\"to\":\"+919876543210\"");
        handler.Requests[0].Body.Should().Contain("\"channel\":\"SMS\"");
    }

    [Fact]
    public async Task The_template_field_name_can_be_corrected_without_a_rebuild()
    {
        // 2Factor's own pages disagree: template_name / template / templateName.
        var (provider, handler, _) = Build(
            options: new TwoFactorOptions { ApiKey = Complete.ApiKey, TemplateNameField = "template" },
            templates: NamedTemplate("LOGIN_OTP"));

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Body.Should().Contain("\"template\":\"LOGIN_OTP\"");
    }

    [Fact]
    public async Task The_template_name_is_read_from_user_configuration_on_every_request()
    {
        // The core requirement: the template name comes from the administrator's configuration
        // during the request, so switching it needs no restart.
        var store = new MutableTemplateStore("FIRST_TEMPLATE");
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.OK, Accepted);
        var provider = new TwoFactorProvider(
            Options.Create(Complete), Options.Create(Policy), saved: null, store,
            new RecordingAuditSink(), new StubHttpClientFactory(handler),
            NullLogger<TwoFactorProvider>.Instance);

        await provider.SendOtpAsync("+919876543210", "4829");

        store.Current = "SECOND_TEMPLATE";
        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Body.Should().Contain("FIRST_TEMPLATE");
        handler.Requests[1].Body.Should().Contain("SECOND_TEMPLATE");
    }

    [Fact]
    public async Task The_otp_variable_name_can_be_corrected_without_a_rebuild()
    {
        var (provider, handler, _) = Build(
            options: new TwoFactorOptions { ApiKey = Complete.ApiKey, OtpVariableName = "otp" });

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Body.Should().Contain("\"otp\":\"4829\"");
    }

    [Fact]
    public async Task The_expiry_variable_is_sent_only_when_the_template_body_uses_it()
    {
        // Placeholder-driven, so a template that never mentions the expiry does not gain a
        // variable 2Factor's approved template has no slot for.
        var withExpiry = Build(templates: NamedTemplate("LOGIN_OTP", "Code {OTP} valid {EXPIRY_MINUTES}"),
            options: new TwoFactorOptions { ApiKey = Complete.ApiKey, ExpiryVariableName = "validity" });
        await withExpiry.Provider.SendOtpAsync("+919876543210", "4829");
        withExpiry.Handler.Requests[0].Body.Should().Contain("\"validity\":\"10\"");

        var withoutExpiry = Build(templates: NamedTemplate("LOGIN_OTP", "Code {OTP}"),
            options: new TwoFactorOptions { ApiKey = Complete.ApiKey, ExpiryVariableName = "validity" });
        await withoutExpiry.Provider.SendOtpAsync("+919876543210", "4829");
        withoutExpiry.Handler.Requests[0].Body.Should().NotContain("validity");
    }

    [Fact]
    public async Task The_native_route_does_not_require_a_registered_template()
    {
        // 2Factor's OTP endpoint generates the code itself, so it works before anyone has
        // registered a DLT template. The previous implementation refused to send at all here.
        var (provider, handler, _) = Build(templates: SmsTestDoubles.Templates());

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        handler.Requests[0].Body.Should().NotContain("template_name");
    }

    [Fact]
    public async Task A_2xx_carrying_an_explicit_error_status_is_still_a_failure()
    {
        // 2Factor reports application failures as HTTP 200 with a Status field.
        var (provider, _, _) = Build(body: """{"Status":"Error","Details":"not approved"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeFalse();
    }

    // ---------------------------------------------------------------- fallback

    [Fact]
    public async Task Falls_back_to_the_transactional_route_when_the_native_route_is_unavailable()
    {
        var (provider, handler, audit) = Build(HttpStatusCode.NotFound, """{"Status":"Error"}""",
            // The native attempt 404s; the transactional fallback then succeeds.
            configure: h => h.Then(HttpStatusCode.OK, Accepted));

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Uri.Should().Be("https://2factor.in/API/V1/OTP/SEND");

        // The transactional route carries the template NAME as a URL segment.
        handler.Requests[1].Uri.Should().Be("https://2factor.in/sms/2fa-secret/LOGIN_OTP");

        audit.Records.Should().HaveCount(2);
        audit.Records[0].AttemptedRoute.Should().Be("native");
        audit.Records[1].AttemptedRoute.Should().Be("transactional");
        audit.Records[1].UsedFallback.Should().BeTrue();
    }

    [Fact]
    public async Task The_transactional_path_url_escapes_a_template_name_containing_spaces()
    {
        // 2Factor's own SDK example uses a human-readable template name with a space.
        var (provider, handler, _) = Build(HttpStatusCode.NotFound, """{"Status":"Error"}""",
            templates: NamedTemplate("Login OTP Approved"),
            configure: h => h.Then(HttpStatusCode.OK, Accepted));

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[1].PathAndQuery.Should().Be("/sms/2fa-secret/Login%20OTP%20Approved");
    }

    [Fact]
    public async Task Does_not_fall_back_when_the_first_attempt_may_already_have_been_delivered()
    {
        // A 5xx is ambiguous: the SMS may have gone out. A second send would cost money and could
        // leave the customer holding one of two codes, so it is reported as retryable instead.
        var (provider, handler, _) = Build(HttpStatusCode.ServiceUnavailable, "gateway down");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.Success.Should().BeFalse();
        result.Retryable.Should().BeTrue();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Does_not_fall_back_when_the_credentials_are_rejected()
    {
        var (provider, handler, _) = Build(HttpStatusCode.Unauthorized, """{"code":"INVALID_API_KEY"}""");

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.ErrorCode.Should().Be("INVALID_API_KEY");
        result.Retryable.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task NativeOtp_mode_never_falls_back()
    {
        var (provider, handler, _) = Build(HttpStatusCode.NotFound, """{"Status":"Error"}""",
            options: new TwoFactorOptions
            {
                ApiKey = Complete.ApiKey,
                DeliveryMode = SmsOtpDeliveryMode.NativeOtp
            });

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task TransactionalTemplate_mode_skips_the_native_route()
    {
        var (provider, handler, audit) = Build(
            options: new TwoFactorOptions
            {
                ApiKey = Complete.ApiKey,
                DeliveryMode = SmsOtpDeliveryMode.TransactionalTemplate
            });

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be("https://2factor.in/sms/2fa-secret/LOGIN_OTP");
        audit.Records[0].AttemptedRoute.Should().Be("transactional");
        audit.Records[0].UsedFallback.Should().BeFalse();
    }

    [Fact]
    public async Task The_transactional_route_refuses_to_send_without_a_template_name()
    {
        // The name is part of the URL here, so there is nothing sensible to send without it.
        var (provider, handler, _) = Build(
            templates: SmsTestDoubles.Templates(),
            options: new TwoFactorOptions
            {
                ApiKey = Complete.ApiKey,
                DeliveryMode = SmsOtpDeliveryMode.TransactionalTemplate
            });

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.ErrorCode.Should().Be("TEMPLATE_NOT_CONFIGURED");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Delivery_mode_can_be_chosen_from_the_administrators_saved_settings()
    {
        var (provider, handler, _) = Build(
            saved: SmsTestDoubles.SavedSettings(
                SmsProviderKind.TwoFactor, true, ("DeliveryMode", "TransactionalTemplate")));

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Uri.Should().Be("https://2factor.in/sms/2fa-secret/LOGIN_OTP");
    }

    [Fact]
    public async Task An_unrecognised_delivery_mode_falls_back_to_the_documented_default()
    {
        var (provider, handler, _) = Build(
            saved: SmsTestDoubles.SavedSettings(
                SmsProviderKind.TwoFactor, true, ("DeliveryMode", "carrier-pigeon")));

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Uri.Should().Be("https://2factor.in/API/V1/OTP/SEND");
    }

    // ---------------------------------------------------------------- configuration

    [Fact]
    public async Task The_otp_path_can_be_corrected_in_configuration_without_a_rebuild()
    {
        var (provider, handler, _) = Build(
            options: new TwoFactorOptions
            {
                ApiKey = Complete.ApiKey,
                BaseUrl = "https://proxy.internal/2fa",
                OtpPath = "/v9/otp"
            });

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Uri.Should().Be("https://proxy.internal/2fa/v9/otp");
    }

    [Fact]
    public async Task A_send_path_saved_before_the_split_is_still_honoured()
    {
        var (provider, handler, _) = Build(
            saved: SmsTestDoubles.SavedSettings(
                SmsProviderKind.TwoFactor, true, ("SendPath", "/v1/sms/send")));

        await provider.SendOtpAsync("+919876543210", "4829");

        handler.Requests[0].Uri.Should().Be("https://2factor.in/v1/sms/send");
    }

    [Fact]
    public async Task A_gateway_failure_is_retryable()
    {
        var (provider, _, _) = Build(HttpStatusCode.ServiceUnavailable, "gateway down");

        (await provider.SendOtpAsync("+919876543210", "4829")).Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_a_number_without_a_country_code()
    {
        var (provider, handler, _) = Build();

        var result = await provider.SendOtpAsync("9876543210", "4829");

        result.ErrorCode.Should().Be("INVALID_PHONE_NUMBER");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_credentials_are_reported_without_calling_the_gateway()
    {
        var (provider, handler, _) = Build(options: new TwoFactorOptions { ApiKey = "" });

        var result = await provider.SendOtpAsync("+919876543210", "4829");

        result.ErrorCode.Should().Be("MISSING_CREDENTIALS");
        handler.Requests.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- auditing

    [Fact]
    public async Task Records_one_audit_entry_per_attempt()
    {
        var (provider, _, audit) = Build(templates: NamedTemplate("LOGIN_OTP"));

        await provider.SendOtpAsync("+919876543210", "4829");

        var record = audit.Records.Should().ContainSingle().Subject;
        record.Provider.Should().Be("2Factor");
        record.Mode.Should().Be("Auto");
        record.AttemptedRoute.Should().Be("native");
        record.UsedFallback.Should().BeFalse();
        record.TemplateName.Should().Be("Test template");
        record.TemplateReference.Should().Be("LOGIN_OTP");
        record.Success.Should().BeTrue();
        record.ProviderMessageId.Should().Be("SESS-1");
        record.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task Audits_a_failure_with_its_error_code_and_retryability()
    {
        var (provider, _, audit) = Build(HttpStatusCode.Unauthorized, """{"code":"INVALID_API_KEY"}""");

        await provider.SendOtpAsync("+919876543210", "4829");

        var record = audit.Records.Should().ContainSingle().Subject;
        record.Success.Should().BeFalse();
        record.ErrorCode.Should().Be("INVALID_API_KEY");
        record.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task The_audit_record_never_carries_the_code_or_the_full_number_or_the_credential()
    {
        var (provider, _, audit) = Build();

        await provider.SendOtpAsync("+919876543210", "4829");

        var record = audit.Records.Should().ContainSingle().Subject;
        var serialized = System.Text.Json.JsonSerializer.Serialize(record);

        serialized.Should().NotContain("4829");          // the OTP
        serialized.Should().NotContain("919876543210");  // the full number
        serialized.Should().NotContain("2fa-secret");    // the API key
        record.MaskedPhone.Should().Be("...3210");
    }

    [Fact]
    public async Task The_audit_records_the_route_the_provider_actually_chose()
    {
        // Auto mode, native answered successfully: the trail must say native, not just "Auto".
        var (provider, _, audit) = Build();

        await provider.SendOtpAsync("+919876543210", "4829");

        audit.Records.Should().ContainSingle()
            .Which.AttemptedRoute.Should().Be("native");
    }

    [Fact]
    public void Reports_its_identity()
    {
        var (provider, _, _) = Build();

        provider.Kind.Should().Be(SmsProviderKind.TwoFactor);
        provider.ProviderName.Should().Be("2Factor");

        // IsOperational is a default interface member, so it is asserted through the interface —
        // which is how the OTP path actually consumes the adapter.
        ((ISmsProvider)provider).IsOperational.Should().BeTrue();
    }

    // ---------------------------------------------------------------- test doubles

    private sealed class RecordingAuditSink : ISmsOtpAuditSink
    {
        public List<SmsOtpAudit> Records { get; } = [];

        public void Record(in SmsOtpAudit audit) => Records.Add(audit);
    }

    private sealed class MutableTemplateStore(string reference) : ISmsTemplateStore
    {
        public string Current { get; set; } = reference;

        public Task<SmsTemplateSnapshot?> GetActiveAsync(
            SmsProviderKind provider, CancellationToken cancellationToken = default)
            => Task.FromResult<SmsTemplateSnapshot?>(
                new SmsTemplateSnapshot(provider, "Mutable", Current, string.Empty));
    }
}
