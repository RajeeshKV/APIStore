namespace KromicCommerce.Contracts.Auth;

/// <summary>
/// Everything a client needs to enforce phone verification in the UI without guessing.
///
/// <para>
/// Carries only what the verification flow itself needs. Which SMS gateway the store uses is
/// deliberately absent: it is irrelevant to the customer's task, and exposing it leaks
/// infrastructure detail to the public storefront. Operators get it from the admin integration
/// status endpoint, where it is needed for support diagnostics.
/// </para>
/// </summary>
/// <param name="VerificationRequired">
/// True only when SMS is enabled, fully configured, and store policy requires a verified
/// phone before checkout. When false the client must not block the user on verification.
/// </param>
/// <param name="PhoneNumber">The verified number currently on the account, canonical E.164, or null.</param>
/// <param name="Verified">Whether that number has been proven to belong to this customer.</param>
/// <param name="PendingPhoneNumber">
/// A number submitted for change but not yet verified, or null. The client should show this as
/// pending rather than as the account's number.
/// </param>
/// <param name="VerificationSatisfied">
/// True when no verification is required, or when a verified number is already on file.
/// This is the single flag a client should gate a checkout button on.
/// </param>
/// <param name="OtpLength">Digits in a code, so the client can render the right input.</param>
/// <param name="OtpExpiryMinutes">How long a freshly sent code stays valid.</param>
/// <param name="ResendCooldownSeconds">Earliest delay before another code may be requested.</param>
public sealed record PhoneVerificationStatusResponse(
    bool VerificationRequired,
    string? PhoneNumber,
    bool Verified,
    string? PendingPhoneNumber,
    bool VerificationSatisfied,
    int OtpLength,
    int OtpExpiryMinutes,
    int ResendCooldownSeconds);