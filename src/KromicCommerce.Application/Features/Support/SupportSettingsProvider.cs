namespace KromicCommerce.Application.Features.Support;

/// <summary>
/// Reads and updates the SupportSettings singleton.
///
/// The row is seeded by migration, but this provider still tolerates its absence: a support
/// instance restored from a partial backup should degrade to defaults rather than fail every
/// read. Creating on first access is safe because the table carries a check constraint
/// pinning <c>Id</c> to the singleton value, so a racing second insert is rejected by the
/// database rather than producing a second configuration row.
/// </summary>
public sealed class SupportSettingsProvider(IApplicationDbContext db)
{
    public async Task<SupportSettings> GetOrCreateAsync(CancellationToken ct = default)
    {
        var existing = await db.SupportSettings
            .FirstOrDefaultAsync(s => s.Id == SupportSettings.SingletonId, ct);

        if (existing is not null) return existing;

        var created = SupportSettings.CreateDefault();
        db.SupportSettings.Add(created);
        await db.SaveChangesAsync(ct);
        return created;
    }
}