namespace KromicCommerce.Domain.Identity;

/// <summary>
/// Maps a stable external identity (e.g. Google subject) to a local User.
/// The ProviderSubject is the stable provider-side identifier (Google: "sub" claim).
/// Email alone is NOT used as the permanent identity key.
/// </summary>
public sealed class ExternalLogin : Entity
{
    private ExternalLogin() { } // EF constructor

    public static ExternalLogin Create(Guid userId, string provider, string providerSubject, string? providerEmail)
        => new()
        {
            UserId = userId,
            Provider = provider.Trim(),
            ProviderSubject = providerSubject.Trim(),
            ProviderEmail = providerEmail?.Trim(),
            LinkedAt = DateTime.UtcNow
        };

    public Guid UserId { get; private set; }
    public string Provider { get; private set; } = string.Empty;        // e.g. "Google"
    public string ProviderSubject { get; private set; } = string.Empty; // Google: "sub" claim
    public string? ProviderEmail { get; private set; }
    public DateTime LinkedAt { get; private set; }

    // Navigation
    public User User { get; private set; } = null!;
}
