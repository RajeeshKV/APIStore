namespace KromicCommerce.Domain.Identity;

/// <summary>
/// Represents a single refresh token session.
/// Raw token is never stored — only the SHA-256 hash.
/// Rotation: each use replaces this token with a new one; ReplacedByTokenId tracks the chain.
/// Reuse detection: if a revoked token is presented again, the entire chain is invalidated.
/// </summary>
public sealed class RefreshToken : Entity
{
    private RefreshToken() { } // EF constructor

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        string? deviceHint = null)
        => new()
        {
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
            DeviceHint = deviceHint
        };

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 hash of the raw token string. Never store the raw token.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    /// <summary>Id of the token that replaced this one during rotation.</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Optional user-agent / device hint for session visibility.</summary>
    public string? DeviceHint { get; private set; }

    // Navigation
    public User User { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Revoke this token, recording the replacement token id for chain tracing.</summary>
    public void Revoke(Guid? replacedByTokenId = null)
    {
        RevokedAt = DateTime.UtcNow;
        ReplacedByTokenId = replacedByTokenId;
    }
}
