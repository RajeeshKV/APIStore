using System.Security.Cryptography;
using System.Text;
using KromicCommerce.Application.Abstractions.Security;
using KromicCommerce.Infrastructure.Configuration;

namespace KromicCommerce.Infrastructure.Security;

/// <summary>
/// Encrypts persisted integration secrets with a stable AES-256-GCM key supplied
/// by the deployment secret store. This avoids dependence on a container-local
/// ASP.NET Data Protection key ring.
/// </summary>
internal sealed class EnvironmentKeySecretProtectionService : ISecretProtectionService
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public EnvironmentKeySecretProtectionService(IOptions<SecretProtectionOptions> options)
    {
        try
        {
            _key = Convert.FromBase64String(options.Value.EncryptionKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "SecretProtection:EncryptionKey must be a base64-encoded 32-byte key.", ex);
        }

        if (_key.Length != 32)
            throw new InvalidOperationException(
                "SecretProtection:EncryptionKey must decode to exactly 32 bytes.");
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Cannot protect an empty secret.", nameof(plaintext));

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var payload = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, payload, NonceSize + ciphertext.Length, TagSize);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            throw new ArgumentException("Cannot unprotect an empty value.", nameof(ciphertext));
        if (!ciphertext.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Secret was not encrypted with the configured environment key.");

        var payload = Convert.FromBase64String(ciphertext[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted secret payload is invalid.");

        var ciphertextLength = payload.Length - NonceSize - TagSize;
        var plaintext = new byte[ciphertextLength];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            payload.AsSpan(0, NonceSize),
            payload.AsSpan(NonceSize, ciphertextLength),
            payload.AsSpan(NonceSize + ciphertextLength, TagSize),
            plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    public string Mask(string? plaintext, int visibleSuffix = 4)
    {
        if (string.IsNullOrWhiteSpace(plaintext)) return "****";
        if (plaintext.Length <= visibleSuffix) return new string('*', plaintext.Length);
        return $"****{plaintext[^visibleSuffix..]}";
    }
}
