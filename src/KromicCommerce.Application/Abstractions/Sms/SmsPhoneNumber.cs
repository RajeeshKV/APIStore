using System.Globalization;

namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// Canonicalisation of customer phone numbers.
/// </summary>
/// <remarks>
/// Customers (and our validators) enter numbers in any common format, but storage uses one
/// canonical form. That removes the class of bugs where an OTP is persisted as
/// <c>+919876543210</c> and then looked up as <c>9876543210</c>.
/// <para>
/// The canonical form is currently India-only: a bare 10-digit national number beginning 6-9,
/// normalised to <c>+91…</c>. This is an application-level constraint applied before any
/// gateway is called, not a property of the gateways — the Twilio adapter itself accepts any
/// international number. Until this class is generalised, <b>only Indian mobile numbers can be
/// verified, whichever provider is active</b>, and the UI must not offer number entry for other
/// regions.
/// </para>
/// </remarks>
public static class SmsPhoneNumber
{
    /// <summary>Default country code applied when a national number is supplied without one.</summary>
    public const string DefaultCountryCode = "91";

    private const int NationalLength = 10;
    private const int WithCountryCodeLength = 12;

    /// <summary>
    /// Strips formatting and returns the bare 10-digit Indian national number.
    /// Returns <c>null</c> when the input is not a usable Indian mobile number.
    /// </summary>
    public static string? TryToNational(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());

        if (digits.Length == WithCountryCodeLength && digits.StartsWith(DefaultCountryCode, StringComparison.Ordinal))
            digits = digits[2..];
        else if (digits.Length > WithCountryCodeLength)
            return null;

        if (digits.Length != NationalLength)
            return null;

        // Indian mobile numbers always start 6-9. Rejecting the rest here stops obviously
        // invalid numbers from being billed as an SMS.
        if (digits[0] is < '6' or > '9')
            return null;

        return digits;
    }

    /// <summary>
    /// Canonical storage form: E.164 without formatting, e.g. <c>+919876543210</c>.
    /// This is what we persist and compare against. Returns <c>null</c> when invalid.
    /// </summary>
    public static string? TryToE164(string? input)
    {
        var national = TryToNational(input);
        return national is null
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"+{DefaultCountryCode}{national}");
    }

    /// <summary>Two numbers are the same customer when their canonical forms match.</summary>
    public static bool AreEquivalent(string? left, string? right)
        => TryToE164(left) is { } a && TryToE164(right) is { } b
           && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>
    /// Last four digits, for logging. Never logs the full number.
    /// </summary>
    public static string Mask(string? phoneNumber)
        => string.IsNullOrEmpty(phoneNumber) || phoneNumber.Length <= 4
            ? "****"
            : $"...{phoneNumber[^4..]}";
}
