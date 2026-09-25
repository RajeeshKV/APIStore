using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KromicCommerce.Infrastructure.Persistence.Configurations;

internal sealed class BusinessSettingsConfiguration : IEntityTypeConfiguration<BusinessSettings>
{
    public void Configure(EntityTypeBuilder<BusinessSettings> builder)
    {
        builder.ToTable("business_settings");
        builder.HasKey(bs => bs.Id);

        // -----------------------------------------------------------------------
        // Basic info
        // -----------------------------------------------------------------------
        builder.Property(bs => bs.BusinessName).IsRequired().HasMaxLength(200);
        builder.Property(bs => bs.LegalName).HasMaxLength(200);
        builder.Property(bs => bs.WebsiteUrl).HasMaxLength(512);
        builder.Property(bs => bs.SupportEmail).HasMaxLength(256);
        builder.Property(bs => bs.SupportPhone).HasMaxLength(20);
        builder.Property(bs => bs.Address).HasMaxLength(500);
        builder.Property(bs => bs.LogoUrl).HasMaxLength(1024);

        // Social links
        builder.Property(bs => bs.FacebookUrl).HasMaxLength(512);
        builder.Property(bs => bs.InstagramUrl).HasMaxLength(512);
        builder.Property(bs => bs.TwitterUrl).HasMaxLength(512);
        builder.Property(bs => bs.YoutubeUrl).HasMaxLength(512);
        builder.Property(bs => bs.WhatsAppNumber).HasMaxLength(20);
        builder.Property(bs => bs.LinkedInUrl).HasMaxLength(512);

        // -----------------------------------------------------------------------
        // Locale
        // -----------------------------------------------------------------------
        builder.Property(bs => bs.CountryCode).IsRequired().HasMaxLength(2);
        builder.Property(bs => bs.CurrencyCode).IsRequired().HasMaxLength(3);
        builder.Property(bs => bs.TimeZoneId).IsRequired().HasMaxLength(100);
        builder.Property(bs => bs.Culture).IsRequired().HasMaxLength(20);

        // -----------------------------------------------------------------------
        // Store status
        // -----------------------------------------------------------------------
        builder.Property(bs => bs.IsStoreOpen).IsRequired().HasDefaultValue(true);
        builder.Property(bs => bs.TemporaryClosureMessage).HasMaxLength(500);

        // -----------------------------------------------------------------------
        // Audit
        // -----------------------------------------------------------------------
        builder.Property(bs => bs.CreatedAtUtc).IsRequired();
        builder.Property(bs => bs.UpdatedAtUtc).IsRequired();

        // -----------------------------------------------------------------------
        // Owned: DeliverySettings (flattened columns — no join)
        // -----------------------------------------------------------------------
        builder.OwnsOne(bs => bs.Delivery, d =>
        {
            d.Property(x => x.FlatFeeAmount)
                .HasColumnName("delivery_flat_fee_amount")
                .HasColumnType("numeric(10,2)")
                .IsRequired()
                .HasDefaultValue(0m);

            d.Property(x => x.FreeShippingThreshold)
                .HasColumnName("delivery_free_shipping_threshold")
                .HasColumnType("numeric(10,2)");

            d.Property(x => x.CodEnabled)
                .HasColumnName("delivery_cod_enabled")
                .IsRequired()
                .HasDefaultValue(true);

            d.Property(x => x.CodExtraFee)
                .HasColumnName("delivery_cod_extra_fee")
                .HasColumnType("numeric(10,2)")
                .IsRequired()
                .HasDefaultValue(0m);

            d.Property(x => x.ProcessingDays)
                .HasColumnName("delivery_processing_days")
                .IsRequired()
                .HasDefaultValue(1);

            d.Property(x => x.MinDeliveryDays)
                .HasColumnName("delivery_min_days")
                .IsRequired()
                .HasDefaultValue(3);

            d.Property(x => x.MaxDeliveryDays)
                .HasColumnName("delivery_max_days")
                .IsRequired()
                .HasDefaultValue(7);
        });

        // -----------------------------------------------------------------------
        // Owned: StoreAuthSettings
        // -----------------------------------------------------------------------
        builder.OwnsOne(bs => bs.Auth, a =>
        {
            a.Property(x => x.GoogleOAuthEnabled)
                .HasColumnName("auth_google_oauth_enabled")
                .IsRequired()
                .HasDefaultValue(false);

            a.Property(x => x.EmailPasswordEnabled)
                .HasColumnName("auth_email_password_enabled")
                .IsRequired()
                .HasDefaultValue(true);

            a.Property(x => x.MobileOtpEnabled)
                .HasColumnName("auth_mobile_otp_enabled")
                .IsRequired()
                .HasDefaultValue(false);

            a.Property(x => x.OtpExpiryMinutes)
                .HasColumnName("auth_otp_expiry_minutes")
                .IsRequired()
                .HasDefaultValue(10);

            a.Property(x => x.OtpResendCooldownSeconds)
                .HasColumnName("auth_otp_resend_cooldown_seconds")
                .IsRequired()
                .HasDefaultValue(60);

            a.Property(x => x.OtpMaxAttempts)
                .HasColumnName("auth_otp_max_attempts")
                .IsRequired()
                .HasDefaultValue(5);

            a.Property(x => x.SmsProvider)
                .HasColumnName("auth_sms_provider")
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("Fast2SMS");
        });

        // -----------------------------------------------------------------------
        // Owned: EmailSettings
        // -----------------------------------------------------------------------
        builder.OwnsOne(bs => bs.Email, e =>
        {
            e.Property(x => x.Mode)
                .HasColumnName("email_mode")
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50)
                .HasDefaultValue(EmailMode.KromicManaged);

            e.Property(x => x.SenderName)
                .HasColumnName("email_sender_name")
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("Store");

            e.Property(x => x.SenderEmail)
                .HasColumnName("email_sender_email")
                .HasMaxLength(256);
        });

        // -----------------------------------------------------------------------
        // Owned: SeoSettings
        // -----------------------------------------------------------------------
        builder.OwnsOne(bs => bs.Seo, s =>
        {
            s.Property(x => x.MetaTitle)
                .HasColumnName("seo_meta_title")
                .HasMaxLength(120);

            s.Property(x => x.MetaDescription)
                .HasColumnName("seo_meta_description")
                .HasMaxLength(320);

            s.Property(x => x.MetaKeywords)
                .HasColumnName("seo_meta_keywords")
                .HasMaxLength(500);

            s.Property(x => x.FaviconUrl)
                .HasColumnName("seo_favicon_url")
                .HasMaxLength(1024);

            s.Property(x => x.OgImageUrl)
                .HasColumnName("seo_og_image_url")
                .HasMaxLength(1024);
        });

        // -----------------------------------------------------------------------
        // Owned: TaxSettings
        // -----------------------------------------------------------------------
        builder.OwnsOne(bs => bs.Tax, t =>
        {
            t.Property(x => x.TaxEnabled)
                .HasColumnName("tax_enabled")
                .IsRequired()
                .HasDefaultValue(false);

            t.Property(x => x.TaxPercentage)
                .HasColumnName("tax_percentage")
                .HasColumnType("numeric(6,4)")
                .IsRequired()
                .HasDefaultValue(0m);

            t.Property(x => x.IsPriceInclusive)
                .HasColumnName("tax_is_price_inclusive")
                .IsRequired()
                .HasDefaultValue(false);

            t.Property(x => x.TaxLabel)
                .HasColumnName("tax_label")
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Tax");
        });
    }
}
