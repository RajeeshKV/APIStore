namespace KromicCommerce.Domain.Identity;

/// <summary>
/// Core identity aggregate. Represents both customers and admins.
/// Business profile details live in CustomerProfile (for customers).
/// Passwords are stored hashed — never in plaintext.
/// TokenVersion enables instant invalidation of all existing JWTs for this user.
/// </summary>
public sealed class User : AuditableEntity
{
    private User() { } // EF constructor

    public static User CreateCustomer(string email, string? passwordHash, string? firstName, string? lastName)
    {
        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            NormalizedEmail = email.Trim().ToUpperInvariant(),
            PasswordHash = passwordHash,
            FirstName = firstName?.Trim(),
            LastName = lastName?.Trim(),
            Role = UserRole.Customer,
            IsActive = true,
            TokenVersion = 1,
            EmailVerifiedAt = null
        };
        user.RaiseDomainEvent(new UserRegisteredEvent(user.Id, user.Email, user.Role));
        return user;
    }

    public static User CreateAdmin(string email, string passwordHash, string firstName, string lastName)
    {
        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            NormalizedEmail = email.Trim().ToUpperInvariant(),
            PasswordHash = passwordHash,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Role = UserRole.Admin,
            IsActive = true,
            TokenVersion = 1,
            EmailVerifiedAt = null
        };
        user.RaiseDomainEvent(new UserRegisteredEvent(user.Id, user.Email, user.Role));
        return user;
    }

    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public string? PasswordHash { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }

    // PhoneNumber, PhoneNumberVerified and PendingPhoneNumber are declared with the phone
    // behaviour below, so their documentation sits next to the rules that maintain them.

    /// <summary>Optional unique username for admin login. Null for customers.</summary>
    public string? Username { get; private set; }
    /// <summary>Normalised (uppercase, trimmed) username for case-insensitive lookup.</summary>
    public string? NormalizedUsername { get; private set; }

    /// <summary>
    /// SHA-256 hash of the current password reset token.
    /// Null when no reset is in progress.
    /// The raw token is sent to the user's email; only the hash is stored.
    /// </summary>
    public string? PasswordResetTokenHash { get; private set; }

    /// <summary>UTC expiry for the active password reset token.</summary>
    public DateTime? PasswordResetTokenExpiresAt { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>
    /// Incrementing version embedded in JWT claims.
    /// Any token with a lower version is rejected immediately (supports logout-all).
    /// </summary>
    public int TokenVersion { get; private set; }

    public DateTime? EmailVerifiedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }

    // Navigation
    public CustomerProfile? CustomerProfile { get; private set; }
    private readonly List<ExternalLogin> _externalLogins = [];
    public IReadOnlyList<ExternalLogin> ExternalLogins => _externalLogins.AsReadOnly();
    private readonly List<RefreshToken> _refreshTokens = [];
    public IReadOnlyList<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void SetUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username must not be empty.", nameof(username));
        var trimmed = username.Trim();
        if (trimmed.Length < 3)
            throw new ArgumentException("Username must be at least 3 characters.", nameof(username));
        if (trimmed.Length > 50)
            throw new ArgumentException("Username must not exceed 50 characters.", nameof(username));
        Username = trimmed;
        NormalizedUsername = trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// Stores a hashed reset token and its expiry.
    /// The raw token must be emailed to the user; never store the raw value.
    /// </summary>
    public void SetPasswordResetToken(string tokenHash, DateTime expiresAt)
    {
        PasswordResetTokenHash = tokenHash;
        PasswordResetTokenExpiresAt = expiresAt;
    }

    /// <summary>Clears the reset token after successful use or expiry.</summary>
    public void ClearPasswordResetToken()
    {
        PasswordResetTokenHash = null;
        PasswordResetTokenExpiresAt = null;
    }

    /// <summary>
    /// Resets the password, increments TokenVersion (invalidates all JWTs),
    /// and clears the reset token. Must be called inside a transaction.
    /// </summary>
    public void ResetPassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        IncrementTokenVersion();
        ClearPasswordResetToken();
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        RaiseDomainEvent(new UserLoggedInEvent(Id, Email));
    }

    public void MarkEmailVerified()
    {
        if (EmailVerifiedAt is null)
            EmailVerifiedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// The single source of truth for this account's phone number — the one checkout, the
    /// verification status endpoint and address auto-fill all read.
    /// </summary>
    /// <remarks>
    /// Only ever set to a number that has been proven, or when SMS cannot verify at all. A
    /// self-declared number belongs in <see cref="PendingPhoneNumber"/>.
    /// </remarks>
    public string? PhoneNumber { get; private set; }

    /// <summary>Whether <see cref="PhoneNumber"/> has been proven to belong to this customer.</summary>
    public bool PhoneNumberVerified { get; private set; }

    /// <summary>
    /// A number the customer asked to change to, awaiting proof of ownership.
    /// </summary>
    /// <remarks>
    /// Held separately from <see cref="PhoneNumber"/> so a verification in progress cannot damage
    /// the verified state. Writing an unproven number straight into <see cref="PhoneNumber"/>
    /// would clear an existing verification immediately — so a customer who began a change and
    /// then abandoned it would be left with no verified number and no way back except finishing
    /// the new verification.
    /// </remarks>
    public string? PendingPhoneNumber { get; private set; }

    /// <summary>
    /// Sets the account phone directly.
    ///
    /// <para>
    /// Changing the number always resets <see cref="PhoneNumberVerified"/>: verification is a
    /// claim about a specific number, so it cannot survive a change to a different one.
    /// Re-submitting the same number preserves an existing verification.
    /// </para>
    /// </summary>
    public void SetPhoneNumber(string phoneNumber, bool verified)
    {
        var normalized = phoneNumber.Trim();
        var unchanged = string.Equals(PhoneNumber, normalized, StringComparison.Ordinal);

        PhoneNumber = normalized;
        PhoneNumberVerified = unchanged ? PhoneNumberVerified && verified : verified;

        // An account that has just been proven verified has nothing pending.
        if (PhoneNumberVerified)
            PendingPhoneNumber = null;
    }

    /// <summary>
    /// Records a number the customer wants to use, without disturbing the current verified
    /// number. The change takes effect only through <see cref="PromotePendingPhoneNumber"/>.
    /// </summary>
    public void RequestPhoneNumberChange(string phoneNumber)
    {
        var normalized = phoneNumber.Trim();

        // Already the verified number: nothing to prove, so do not ask the customer to verify a
        // number we have already verified.
        if (PhoneNumberVerified
            && string.Equals(PhoneNumber, normalized, StringComparison.Ordinal))
        {
            PendingPhoneNumber = null;
            return;
        }

        PendingPhoneNumber = normalized;
    }

    /// <summary>
    /// Promotes the pending number to the verified account number. Called only once an OTP sent
    /// to <paramref name="verifiedPhone"/> has been accepted.
    /// </summary>
    public void PromotePendingPhoneNumber(string verifiedPhone)
    {
        PhoneNumber = verifiedPhone.Trim();
        PhoneNumberVerified = true;
        PendingPhoneNumber = null;
    }

    /// <summary>
    /// Abandons a pending change, leaving the verified number exactly as it was.
    /// </summary>
    public void ClearPendingPhoneNumber() => PendingPhoneNumber = null;

    /// <summary>
    /// Increment TokenVersion to invalidate all existing access tokens.
    /// Used by logout-all and password change flows.
    /// </summary>
    public void IncrementTokenVersion() => TokenVersion++;

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    public void AddExternalLogin(ExternalLogin login) => _externalLogins.Add(login);

    public string FullName =>
        string.Join(" ", new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
