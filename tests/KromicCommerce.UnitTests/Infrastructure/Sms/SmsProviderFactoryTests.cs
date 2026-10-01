using System.Net;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Infrastructure.Sms;
using KromicCommerce.UnitTests.Application;

namespace KromicCommerce.UnitTests.Infrastructure.Sms;

public sealed class SmsProviderFactoryTests
{
    private static SmsProviderFactory Build(SmsOptions options) => new(
        Options.Create(options),
        Options.Create(new TwoFactorOptions()),
        Options.Create(new Free2SmsOptions()),
        Options.Create(new TwilioOptions()),
        Options.Create(new SmsOtpPolicyOptions()),
        SmsTestDoubles.NoSavedSettings(),
        SmsTestDoubles.Templates(),
        new StubHttpClientFactory(new StubHttpMessageHandler(HttpStatusCode.OK, "{}")),
        NullLoggerFactory.Instance);

    [Fact]
    public void Selects_2factor_when_it_is_the_configured_provider()
    {
        var factory = Build(new SmsOptions
        {
            Enabled = true,
            Provider = "2Factor",
            TwoFactor = new TwoFactorOptions { ApiKey = "k" }
        });

        factory.Create().Kind.Should().Be(SmsProviderKind.TwoFactor);
        factory.Status.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Selects_free2sms_when_it_is_the_configured_provider()
    {
        var factory = Build(new SmsOptions
        {
            Enabled = true,
            Provider = "Free2Sms",
            Free2Sms = new Free2SmsOptions { ApiKey = "k", SenderId = "F2SMS" }
        });

        factory.Create().Kind.Should().Be(SmsProviderKind.Free2Sms);
    }

    [Fact]
    public void Selects_twilio_when_it_is_the_configured_provider()
    {
        var factory = Build(new SmsOptions
        {
            Enabled = true,
            Provider = "Twilio",
            Twilio = new TwilioOptions { AccountSid = "AC", AuthToken = "t", ServiceSid = "VA" }
        });

        factory.Create().Kind.Should().Be(SmsProviderKind.Twilio);
    }

    [Theory]
    [InlineData("TechTo")]
    [InlineData("SmsLocal")]
    [InlineData("Fast2SMS")]
    public void A_removed_provider_name_yields_a_non_operational_no_op_instead_of_throwing(string name)
    {
        var factory = Build(new SmsOptions { Enabled = true, Provider = name });

        var provider = factory.Create();

        provider.Kind.Should().Be(SmsProviderKind.None);
        provider.IsOperational.Should().BeFalse();
        factory.Status.IsConfigured.Should().BeFalse();
        factory.Status.MissingSettings.Should().Contain("Sms:Provider");
    }

    [Fact]
    public void Incomplete_credentials_yield_a_non_operational_no_op()
    {
        var factory = Build(new SmsOptions
        {
            Enabled = true,
            Provider = "Free2Sms",
            Free2Sms = new Free2SmsOptions { ApiKey = "k" } // SenderId missing
        });

        factory.Create().IsOperational.Should().BeFalse();
        factory.Status.MissingSettings.Should().Contain("Sms:Free2Sms:SenderId");
    }

    [Fact]
    public async Task The_no_op_provider_reports_a_configuration_error_rather_than_sending()
    {
        var provider = Build(new SmsOptions()).Create();

        var result = await provider.SendOtpAsync("9876543210", "123456");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SMS_NOT_CONFIGURED");
    }

    [Fact]
    public void Only_the_selected_provider_is_ever_instantiated()
    {
        // Twilio has no credentials, Free2SMS is complete. Selecting Free2SMS must work —
        // the incomplete selection of another provider must not block it.
        var factory = Build(new SmsOptions
        {
            Enabled = true,
            Provider = "Free2Sms",
            Twilio = new TwilioOptions(),
            Free2Sms = new Free2SmsOptions { ApiKey = "k", SenderId = "F2SMS" }
        });

        factory.Create().Kind.Should().Be(SmsProviderKind.Free2Sms);
        factory.Status.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void RequiresVerification_only_while_sms_is_usable()
    {
        Build(new SmsOptions
        {
            Enabled = true,
            RequireVerifiedPhoneAtCheckout = true,
            Provider = "Twilio",
            Twilio = new TwilioOptions { AccountSid = "AC", AuthToken = "t", ServiceSid = "VA" }
        }).Status.RequiresVerification.Should().BeTrue();

        // Policy asks for it, but nothing can deliver a code — requiring it would lock
        // every customer out of checkout.
        Build(new SmsOptions { Enabled = false, RequireVerifiedPhoneAtCheckout = true })
            .Status.RequiresVerification.Should().BeFalse();
    }
}
