namespace KromicCommerce.Domain.Store;

/// <summary>
/// Single-row business configuration record.
/// Each deployment has exactly one row (Id = SingletonId).
/// Business settings are managed via Admin UI, not environment variables.
///
/// Owned value objects (Delivery, Auth, Email, Seo) are stored as flattened
/// columns in the same table — no joins required on the read path.
/// </summary>
public sealed class BusinessSettings : AuditableEntity
{
    /// <summary>Singleton row ID — every deployment uses this fixed value.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    private BusinessSettings() { } // EF constructor

    public static BusinessSettings CreateDefault(string businessName)
        => new()
        {
            Id = SingletonId,
            BusinessName = businessName.Trim(),
            CountryCode = "IN",
            CurrencyCode = "INR",
            TimeZoneId = "Asia/Kolkata",
            Culture = "en-IN",
            IsStoreOpen = true,
            TemporaryClosureMessage = null,
            Delivery = DeliverySettings.Default(),
            Auth = StoreAuthSettings.Default(),
            Email = EmailSettings.Default(),
            Seo = SeoSettings.Default(),
            Tax = TaxSettings.Default()
        };

    // -----------------------------------------------------------------------
    // Basic info
    // -----------------------------------------------------------------------
    public string BusinessName { get; private set; } = string.Empty;
    public string? LegalName { get; private set; }
    public string? WebsiteUrl { get; private set; }
    public string? SupportEmail { get; private set; }
    public string? SupportPhone { get; private set; }
    public string? Address { get; private set; }
    public string? LogoUrl { get; private set; }

    // Social links
    public string? FacebookUrl { get; private set; }
    public string? InstagramUrl { get; private set; }
    public string? TwitterUrl { get; private set; }
    public string? YoutubeUrl { get; private set; }
    public string? WhatsAppNumber { get; private set; }
    public string? LinkedInUrl { get; private set; }

    // -----------------------------------------------------------------------
    // Locale
    // -----------------------------------------------------------------------
    public string CountryCode { get; private set; } = "IN";        // ISO 3166-1 alpha-2
    public string CurrencyCode { get; private set; } = "INR";      // ISO 4217
    public string TimeZoneId { get; private set; } = "Asia/Kolkata"; // IANA
    public string Culture { get; private set; } = "en-IN";

    // -----------------------------------------------------------------------
    // Store status
    // -----------------------------------------------------------------------
    public bool IsStoreOpen { get; private set; } = true;
    public string? TemporaryClosureMessage { get; private set; }

    // -----------------------------------------------------------------------
    // Owned value objects — flattened into this table
    // -----------------------------------------------------------------------
    public DeliverySettings Delivery { get; private set; } = DeliverySettings.Default();
    public StoreAuthSettings Auth { get; private set; } = StoreAuthSettings.Default();
    public EmailSettings Email { get; private set; } = EmailSettings.Default();
    public SeoSettings Seo { get; private set; } = SeoSettings.Default();
    public TaxSettings Tax { get; private set; } = TaxSettings.Default();

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void UpdateBasicInfo(
        string businessName,
        string? legalName,
        string? websiteUrl,
        string? supportEmail,
        string? supportPhone,
        string? address)
    {
        if (string.IsNullOrWhiteSpace(businessName))
            throw new ArgumentException("Business name must not be empty.", nameof(businessName));

        BusinessName = businessName.Trim();
        LegalName = legalName?.Trim();
        WebsiteUrl = websiteUrl?.Trim();
        SupportEmail = supportEmail?.Trim();
        SupportPhone = supportPhone?.Trim();
        Address = address?.Trim();
    }

    public void UpdateSocialLinks(
        string? facebook,
        string? instagram,
        string? twitter,
        string? youtube,
        string? whatsApp = null,
        string? linkedIn = null)
    {
        FacebookUrl = facebook?.Trim();
        InstagramUrl = instagram?.Trim();
        TwitterUrl = twitter?.Trim();
        YoutubeUrl = youtube?.Trim();
        WhatsAppNumber = whatsApp?.Trim();
        LinkedInUrl = linkedIn?.Trim();
    }

    public void UpdateLocale(
        string countryCode,
        string currencyCode,
        string timeZoneId,
        string culture)
    {
        if (string.IsNullOrWhiteSpace(countryCode)) throw new ArgumentException("Country code required.", nameof(countryCode));
        if (string.IsNullOrWhiteSpace(currencyCode)) throw new ArgumentException("Currency code required.", nameof(currencyCode));
        if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ArgumentException("Timezone required.", nameof(timeZoneId));
        if (string.IsNullOrWhiteSpace(culture)) throw new ArgumentException("Culture required.", nameof(culture));

        CountryCode = countryCode.Trim().ToUpperInvariant();
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        TimeZoneId = timeZoneId.Trim();
        Culture = culture.Trim();
    }

    public void SetStoreOpen(bool isOpen, string? closureMessage = null)
    {
        IsStoreOpen = isOpen;
        TemporaryClosureMessage = isOpen ? null : closureMessage?.Trim();
    }

    public void SetLogoUrl(string? url) => LogoUrl = url?.Trim();

    public void UpdateTax(TaxSettings tax) =>
        Tax = tax ?? throw new ArgumentNullException(nameof(tax));

    public void UpdateDelivery(DeliverySettings delivery) =>
        Delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));

    public void UpdateAuth(StoreAuthSettings auth) =>
        Auth = auth ?? throw new ArgumentNullException(nameof(auth));

    public void UpdateEmail(EmailSettings email) =>
        Email = email ?? throw new ArgumentNullException(nameof(email));

    public void UpdateSeo(SeoSettings seo) =>
        Seo = seo ?? throw new ArgumentNullException(nameof(seo));
}
