using KromicCommerce.Application.Abstractions.Security;
using Microsoft.AspNetCore.DataProtection;

namespace KromicCommerce.Infrastructure.Security;

/// <summary>
/// ISecretProtectionService implementation using ASP.NET Core Data Protection.
/// Secrets are encrypted with a purpose-scoped key derived from the application's
/// data protection key ring.
/// Keys are stored in the default location (file system or Redis-backed in future).
/// Rotation is handled automatically by ASP.NET Core.
/// Never log the plaintext or ciphertext values.
/// </summary>
internal sealed class DataProtectionSecretService(
    IDataProtectionProvider dataProtectionProvider) : ISecretProtectionService
{
    private const string Purpose = "KromicCommerce.IntegrationSecrets.v1";
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(Purpose);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Cannot protect an empty secret.", nameof(plaintext));

        return _protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            throw new ArgumentException("Cannot unprotect an empty value.", nameof(ciphertext));

        return _protector.Unprotect(ciphertext);
    }

    public string Mask(string? plaintext, int visibleSuffix = 4)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            return "****";

        if (plaintext.Length <= visibleSuffix)
            return new string('*', plaintext.Length);

        var suffix = plaintext[^visibleSuffix..];
        return $"****{suffix}";
    }
}
