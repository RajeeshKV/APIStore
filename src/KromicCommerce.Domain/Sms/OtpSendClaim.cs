using KromicCommerce.Domain.Identity;

namespace KromicCommerce.Domain.Sms;

/// <summary>
/// A short-lived claim that one request holds the right to send an OTP for a given
/// phone number and purpose.
/// </summary>
/// <remarks>
/// <para>
/// The resend cooldown is enforced by reading the previous <see cref="OtpRequest"/>, and that
/// read-then-write sequence was not safe: two concurrent requests could both observe "no recent
/// code", both send an SMS, and both write a row — so a customer pressing "resend" twice quickly
/// received two codes despite the cooldown.
/// </para>
/// <para>
/// An in-memory lock cannot fix this, because the API may run as several instances that do not
/// share memory. The claim is instead a row with a UNIQUE index on
/// (PhoneNumber, Purpose): both requests may pass any read-based check, but only one INSERT can
/// succeed, and the loser's insert is rejected by the database. The index — not application
/// logic — is the authoritative gate.
/// </para>
/// <para>
/// The claim exists only for the duration of one send and is deleted in a <c>finally</c>. It is
/// <b>not</b> a second cooldown mechanism: the cooldown itself still comes from the
/// <see cref="OtpRequest"/> row, exactly as before. This row only serialises the
/// check → send → insert sequence.
/// </para>
/// </remarks>
public sealed class OtpSendClaim : Entity
{
    private OtpSendClaim() { } // EF constructor

    public static OtpSendClaim Create(string phoneNumber, OtpPurpose purpose)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Phone number is required.", nameof(phoneNumber));

        return new()
        {
            PhoneNumber = phoneNumber.Trim(),
            Purpose = purpose,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Canonical E.164 form, matching the <see cref="OtpRequest"/> it guards.</summary>
    public string PhoneNumber { get; private set; } = string.Empty;

    public OtpPurpose Purpose { get; private set; }

    /// <summary>
    /// When the claim was taken. A claim older than the staleness window is assumed to belong to
    /// an instance that died mid-send, and may be taken over.
    /// </summary>
    public DateTime CreatedAt { get; private set; }
}