namespace KromicCommerce.UnitTests.Domain;

/// <summary>
/// Guards the in-place mutation contract that every EF Core owned value object on
/// <see cref="BusinessSettings"/> depends on.
///
/// THE BUG THIS EXISTS TO CATCH
/// -----------------------------
/// All six settings groups (Delivery, Auth, Email, Seo, Tax, Payment) are mapped with
/// <c>OwnsOne</c>, and all six value objects derive from <see cref="ValueObject"/>, which
/// overrides <c>Equals</c> structurally. Assigning a brand-new instance to the owned property
/// (<c>BusinessSettings.Delivery = DeliverySettings.Create(...)</c>) can go UNDETECTED by EF's
/// change tracker, so SaveChanges issues no UPDATE and the edit is silently discarded — while
/// the endpoint still returns HTTP 200.
///
/// That is not hypothetical: a live bug where the admin saved
/// <c>PUT /api/v1/admin/settings/delivery</c> with <c>codEnabled: false</c>, received 200 OK,
/// and found the database column still reading <c>true</c> with <c>updatedAtUtc</c> unchanged.
///
/// Mutating the tracked instance in place changes scalar properties, which EF detects
/// unambiguously against its original-value snapshot.
///
/// These tests assert instance identity — the cheapest possible canary. A mocked DbContext can
/// never reproduce EF change detection, but it CAN assert the mutation strategy this class
/// depends on, so reintroducing the reference swap fails fast here rather than in production.
/// </summary>
public sealed class BusinessSettingsOwnedValueObjectMutationTests
{
    // -----------------------------------------------------------------------
    // Delivery — the COD / shipping group that failed in production
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateDelivery_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        var before = settings.Delivery;
        settings.UpdateDelivery(DeliverySettings.Create(
            flatFeeAmount: 10m, freeShippingThreshold: 499m,
            codEnabled: false, codExtraFee: 0m,
            processingDays: 1, minDeliveryDays: 3, maxDeliveryDays: 7));

        settings.Delivery.Should().BeSameAs(before,
            "replacing the reference lets EF skip the change — the owned instance must be reused");
        settings.Delivery.CodEnabled.Should().BeFalse();
        settings.Delivery.FlatFeeAmount.Should().Be(10m);
        settings.Delivery.FreeShippingThreshold.Should().Be(499m);
    }

    [Fact]
    public void SetCodEnabled_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");
        settings.UpdateDelivery(DeliverySettings.Create(
            10m, 499m, codEnabled: true, codExtraFee: 100m, 1, 3, 7));

        var before = settings.Delivery;
        settings.SetCodEnabled(false);

        settings.Delivery.Should().BeSameAs(before);
        settings.Delivery.CodEnabled.Should().BeFalse();
        settings.Delivery.CodExtraFee.Should().Be(100m, "toggling COD must preserve the fee");
    }

    // -----------------------------------------------------------------------
    // The remaining owned groups — same defect, same contract
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateAuth_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        var before = settings.Auth;
        settings.UpdateAuth(StoreAuthSettings.Create(
            googleOAuthEnabled: true, emailPasswordEnabled: false,
            mobileOtpEnabled: true, otpExpiryMinutes: 15,
            otpResendCooldownSeconds: 30, otpMaxAttempts: 3, smsProvider: "Twilio"));

        settings.Auth.Should().BeSameAs(before);
        settings.Auth.GoogleOAuthEnabled.Should().BeTrue();
        settings.Auth.MobileOtpEnabled.Should().BeTrue();
        settings.Auth.SmsProvider.Should().Be("Twilio");
    }

    [Fact]
    public void UpdateAuth_refuses_a_provider_that_is_no_longer_supported()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        // A removed integration must not be storable — otherwise the admin surface keeps
        // offering a gateway that can never send.
        ((Action)(() => settings.UpdateAuth(StoreAuthSettings.Create(
            googleOAuthEnabled: false, emailPasswordEnabled: true,
            mobileOtpEnabled: true, otpExpiryMinutes: 10,
            otpResendCooldownSeconds: 60, otpMaxAttempts: 5, smsProvider: "Fast2SMS"))))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateEmail_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        var before = settings.Email;
        // CustomerBrevo is the mode that accepts a sender email; KromicManaged deliberately
        // discards one (it supplies the sender itself).
        settings.UpdateEmail(EmailSettings.Create(
            EmailMode.CustomerBrevo, "Kromic Store", "hello@shopey.tech"));

        settings.Email.Should().BeSameAs(before);
        settings.Email.SenderEmail.Should().Be("hello@shopey.tech");
    }

    [Fact]
    public void UpdateSeo_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        var before = settings.Seo;
        settings.UpdateSeo(SeoSettings.Create(
            "Title", "Description", "keywords", null, null));

        settings.Seo.Should().BeSameAs(before);
        settings.Seo.MetaTitle.Should().Be("Title");
    }

    [Fact]
    public void UpdateTax_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        var before = settings.Tax;
        settings.UpdateTax(TaxSettings.Create(
            taxEnabled: true, taxPercentage: 18m, isPriceInclusive: true, taxLabel: "GST"));

        settings.Tax.Should().BeSameAs(before);
        settings.Tax.TaxEnabled.Should().BeTrue();
        settings.Tax.TaxPercentage.Should().Be(18m);
    }

    // -----------------------------------------------------------------------
    // Credential helpers — these previously swapped Auth/Payment references directly
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateGoogleCredentials_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        var before = settings.Auth;
        settings.UpdateGoogleCredentials("client-id.apps.googleusercontent.com", "encrypted", null);

        settings.Auth.Should().BeSameAs(before);
        settings.Auth.GoogleClientId.Should().Be("client-id.apps.googleusercontent.com");
        settings.Auth.EncryptedGoogleClientSecret.Should().Be("encrypted");
        settings.Auth.GoogleOAuthEnabled.Should().BeTrue(
            "storing credentials is expected to activate the feature");
    }

    [Fact]
    public void ClearGoogleCredentials_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");
        settings.UpdateGoogleCredentials("client-id", "encrypted", null);

        var before = settings.Auth;
        settings.ClearGoogleCredentials();

        settings.Auth.Should().BeSameAs(before);
        settings.Auth.GoogleClientId.Should().BeNull();
        settings.Auth.GoogleOAuthEnabled.Should().BeFalse();
    }

    [Fact]
    public void UpdatePaymentCredentials_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        var before = settings.Payment;
        settings.UpdatePaymentCredentials("rzp_test_123", "enc-key", "enc-webhook");

        settings.Payment.Should().BeSameAs(before);
        settings.Payment.RazorpayKeyId.Should().Be("rzp_test_123");
        settings.Payment.Enabled.Should().BeTrue();
    }

    [Fact]
    public void SetPaymentEnabled_mutates_the_tracked_instance_in_place()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");
        settings.UpdatePaymentCredentials("rzp_test_123", "enc-key", "enc-webhook");

        var before = settings.Payment;
        settings.SetPaymentEnabled(false);

        settings.Payment.Should().BeSameAs(before);
        settings.Payment.Enabled.Should().BeFalse();
        settings.Payment.RazorpayKeyId.Should().Be("rzp_test_123", "disabling keeps credentials");
    }

    /// <summary>
    /// Null guards must survive the change from a throwing expression-bodied setter to a
    /// statement body using ArgumentNullException.ThrowIfNull.
    /// </summary>
    [Fact]
    public void Update_methods_still_reject_null()
    {
        var settings = BusinessSettings.CreateDefault("Kromic Store");

        var delivery = () => settings.UpdateDelivery(null!);
        var auth = () => settings.UpdateAuth(null!);
        var email = () => settings.UpdateEmail(null!);
        var seo = () => settings.UpdateSeo(null!);
        var tax = () => settings.UpdateTax(null!);

        delivery.Should().Throw<ArgumentNullException>();
        auth.Should().Throw<ArgumentNullException>();
        email.Should().Throw<ArgumentNullException>();
        seo.Should().Throw<ArgumentNullException>();
        tax.Should().Throw<ArgumentNullException>();
    }
}
