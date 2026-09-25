using KromicCommerce.Domain.Store;

namespace KromicCommerce.UnitTests.Domain;

public sealed class BusinessSettingsTests
{
    // -----------------------------------------------------------------------
    // Factory
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateDefault_initialises_with_sensible_defaults()
    {
        var settings = BusinessSettings.CreateDefault("My Store");

        settings.BusinessName.Should().Be("My Store");
        settings.CountryCode.Should().Be("IN");
        settings.CurrencyCode.Should().Be("INR");
        settings.IsStoreOpen.Should().BeTrue();
        settings.Id.Should().Be(BusinessSettings.SingletonId);
        settings.Delivery.Should().NotBeNull();
        settings.Auth.Should().NotBeNull();
        settings.Email.Should().NotBeNull();
        settings.Seo.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // UpdateBasicInfo
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateBasicInfo_persists_all_fields()
    {
        var settings = BusinessSettings.CreateDefault("Old Name");

        settings.UpdateBasicInfo("New Name", "New Legal", "https://example.com",
            "support@example.com", "+919999999999", "123 Street");

        settings.BusinessName.Should().Be("New Name");
        settings.LegalName.Should().Be("New Legal");
        settings.WebsiteUrl.Should().Be("https://example.com");
        settings.SupportEmail.Should().Be("support@example.com");
        settings.SupportPhone.Should().Be("+919999999999");
        settings.Address.Should().Be("123 Street");
    }

    [Fact]
    public void UpdateBasicInfo_trims_business_name()
    {
        var settings = BusinessSettings.CreateDefault("X");
        settings.UpdateBasicInfo("  Trimmed  ", null, null, null, null, null);
        settings.BusinessName.Should().Be("Trimmed");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateBasicInfo_throws_for_empty_business_name(string name)
    {
        var settings = BusinessSettings.CreateDefault("X");
        var act = () => settings.UpdateBasicInfo(name, null, null, null, null, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateSocialLinks_persists_urls()
    {
        var settings = BusinessSettings.CreateDefault("X");
        settings.UpdateSocialLinks("https://facebook.com/x", "https://instagram.com/x", null, null);
        settings.FacebookUrl.Should().Be("https://facebook.com/x");
        settings.InstagramUrl.Should().Be("https://instagram.com/x");
        settings.TwitterUrl.Should().BeNull();
        settings.YoutubeUrl.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // UpdateLocale
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateLocale_uppercases_country_and_currency()
    {
        var settings = BusinessSettings.CreateDefault("X");
        settings.UpdateLocale("us", "usd", "America/New_York", "en-US");
        settings.CountryCode.Should().Be("US");
        settings.CurrencyCode.Should().Be("USD");
        settings.TimeZoneId.Should().Be("America/New_York");
        settings.Culture.Should().Be("en-US");
    }

    [Theory]
    [InlineData("", "USD", "UTC", "en-US")]
    [InlineData("US", "", "UTC", "en-US")]
    [InlineData("US", "USD", "", "en-US")]
    [InlineData("US", "USD", "UTC", "")]
    public void UpdateLocale_throws_for_empty_fields(string cc, string cur, string tz, string culture)
    {
        var settings = BusinessSettings.CreateDefault("X");
        var act = () => settings.UpdateLocale(cc, cur, tz, culture);
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // SetStoreOpen
    // -----------------------------------------------------------------------

    [Fact]
    public void SetStoreOpen_false_persists_closure_message()
    {
        var settings = BusinessSettings.CreateDefault("X");
        settings.SetStoreOpen(false, "Back tomorrow");
        settings.IsStoreOpen.Should().BeFalse();
        settings.TemporaryClosureMessage.Should().Be("Back tomorrow");
    }

    [Fact]
    public void SetStoreOpen_true_clears_closure_message()
    {
        var settings = BusinessSettings.CreateDefault("X");
        settings.SetStoreOpen(false, "Closed");
        settings.SetStoreOpen(true);
        settings.IsStoreOpen.Should().BeTrue();
        settings.TemporaryClosureMessage.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // DeliverySettings
    // -----------------------------------------------------------------------

    [Fact]
    public void UpdateDelivery_stores_new_value_object()
    {
        var settings = BusinessSettings.CreateDefault("X");
        var delivery = DeliverySettings.Create(50m, 500m, true, 20m, 1, 3, 7);
        settings.UpdateDelivery(delivery);

        settings.Delivery.FlatFeeAmount.Should().Be(50m);
        settings.Delivery.FreeShippingThreshold.Should().Be(500m);
        settings.Delivery.CodEnabled.Should().BeTrue();
        settings.Delivery.CodExtraFee.Should().Be(20m);
    }

    [Theory]
    [InlineData(-1, 500, 0, 3, 7)]    // negative flat fee
    [InlineData(0, -1, 0, 3, 7)]      // negative threshold
    [InlineData(0, 500, 0, 7, 3)]     // max < min
    public void DeliverySettings_Create_throws_for_invalid_values(
        decimal flat, decimal threshold, decimal codFee, int minDays, int maxDays)
    {
        var act = () => DeliverySettings.Create(flat, threshold, true, codFee, 1, minDays, maxDays);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateDelivery_throws_for_null()
    {
        var settings = BusinessSettings.CreateDefault("X");
        var act = () => settings.UpdateDelivery(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // StoreAuthSettings
    // -----------------------------------------------------------------------

    [Fact]
    public void StoreAuthSettings_Create_validates_otp_expiry()
    {
        var act = () => StoreAuthSettings.Create(false, true, false, 0, 60, 5, "Fast2SMS");
        act.Should().Throw<ArgumentException>().WithMessage("*1 minute*");
    }

    [Fact]
    public void StoreAuthSettings_Create_validates_empty_sms_provider()
    {
        var act = () => StoreAuthSettings.Create(false, true, false, 5, 60, 5, "");
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // EmailSettings
    // -----------------------------------------------------------------------

    [Fact]
    public void EmailSettings_CustomerBrevo_requires_sender_email()
    {
        var act = () => EmailSettings.Create(EmailMode.CustomerBrevo, "My Store", null);
        act.Should().Throw<ArgumentException>().WithMessage("*required in CustomerBrevo*");
    }

    [Fact]
    public void EmailSettings_KromicManaged_does_not_require_sender_email()
    {
        var es = EmailSettings.Create(EmailMode.KromicManaged, "My Store", null);
        es.Mode.Should().Be(EmailMode.KromicManaged);
        es.SenderEmail.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // ValueObject equality
    // -----------------------------------------------------------------------

    [Fact]
    public void DeliverySettings_equality_by_value()
    {
        var a = DeliverySettings.Create(50m, null, true, 0m, 1, 3, 7);
        var b = DeliverySettings.Create(50m, null, true, 0m, 1, 3, 7);
        a.Should().Be(b);
    }

    [Fact]
    public void DeliverySettings_inequality_on_different_values()
    {
        var a = DeliverySettings.Create(50m, null, true, 0m, 1, 3, 7);
        var b = DeliverySettings.Create(99m, null, true, 0m, 1, 3, 7);
        a.Should().NotBe(b);
    }
}
