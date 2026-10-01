using System.Text.Json;
using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Application.Abstractions.Security;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Domain.Sms;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Database-backed <see cref="ISmsProviderSettings"/>: the administrator's saved selection and
/// credentials, decrypted only for the caller that is about to use them.
/// </summary>
/// <remarks>
/// A read failure must not silently fall back to configuration — that would make a misconfigured
/// admin save invisible and send through the wrong gateway. The exception propagates instead.
/// </remarks>
internal sealed class DbSmsProviderSettings(
    IApplicationDbContext db,
    ISecretProtectionService secrets) : ISmsProviderSettings
{
    public async Task<SmsProviderSettingsSnapshot?> GetEffectiveAsync(
        CancellationToken cancellationToken = default)
    {
        var row = await db.SmsProviderConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == SmsProviderConfig.SingletonId, cancellationToken);

        if (row is null)
            return null;

        var settings = ReadProtectedValues(row.EncryptedSettings);

        return new SmsProviderSettingsSnapshot(row.Enabled, row.Kind, settings);
    }

    private IReadOnlyDictionary<string, string> ReadProtectedValues(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            // A corrupt payload means the admin's saved credentials cannot be read. Treating it as
            // "no credentials" would silently fall through to configuration, so it is surfaced.
            throw new InvalidOperationException(
                "The stored SMS provider settings could not be read. Re-save the SMS integration " +
                "configuration to rewrite them.");
        }

        if (parsed is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, protectedValue) in parsed)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
                continue;

            result[key] = secrets.Unprotect(protectedValue);
        }

        return result;
    }
}
