namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Stable master key for encrypting customer-configurable integration secrets.
/// Set SecretProtection__EncryptionKey to a base64-encoded 32-byte value in the
/// deployment secret store. The value must remain stable across redeployments.
/// </summary>
public sealed class SecretProtectionOptions
{
    public const string SectionName = "SecretProtection";

    public string EncryptionKey { get; init; } = string.Empty;
}
