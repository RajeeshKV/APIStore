namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// 2Factor (2factor.in) SMS credentials.
/// </summary>
/// <remarks>
/// <para>
/// This adapter sends only the OTP code that this application generates — it does not use
/// 2Factor's hosted-OTP product. The API key authenticates the request, and the template name
/// selects the registered message template. The OTP is passed as the first positional variable.
/// </para>
/// <para>
/// Reference: 2Factor's OTP SMS API (<c>POST https://2factor.in/API/V1/OTP/SEND</c>).
/// </para>
/// </remarks>
public sealed class TwoFactorOptions
{
    /// <summary>The 2Factor API key. Sent as the X-API-Key header; never logged or returned.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>The template name registered in the 2Factor portal.</summary>
    public string TemplateName { get; init; } = string.Empty;
}
