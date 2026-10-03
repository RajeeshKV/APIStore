namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// 2Factor (2factor.in) SMS credentials.
/// </summary>
/// <remarks>
/// <para>
/// This adapter sends only the OTP code that this application generates — it does not use
/// 2Factor's hosted-OTP product. 2Factor's Manual OTP API takes everything in the request path:
/// <c>POST https://2factor.in/API/V1/{api_key}/SMS/{phone_number}/{otp_code}/{template_name}</c>.
/// There is no key header and no request body.
/// </para>
/// </remarks>
public sealed class TwoFactorOptions
{
    /// <summary>The 2Factor API key. Part of the request URL; never logged or returned.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>The template name registered in the 2Factor portal.</summary>
    public string TemplateName { get; init; } = string.Empty;
}
