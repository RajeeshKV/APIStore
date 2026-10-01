using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Domain.Sms;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Database-backed <see cref="ISmsTemplateStore"/>.
/// </summary>
/// <remarks>
/// No tracking and a single-row projection: this runs on the OTP send path, so it must read one
/// row and must not leave a tracked entity attached to the request's <see cref="DbContext"/>,
/// which would otherwise be saved as part of an unrelated write.
/// </remarks>
internal sealed class EfSmsTemplateStore(IApplicationDbContext db) : ISmsTemplateStore
{
    public async Task<SmsTemplateSnapshot?> GetActiveAsync(
        SmsProviderKind provider, CancellationToken cancellationToken = default)
    {
        var set = AsNoTracking(db.SmsTemplates);

        var row = await set
            .Where(t => t.Provider == provider && t.IsActive)
            // Newest wins if data ever ends up with two active rows for one provider, so a send
            // is never blocked by a stale template. The application layer prevents this state.
            .OrderByDescending(t => t.UpdatedAtUtc)
            .Select(t => new { t.Name, t.ExternalTemplateId, t.Body })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new SmsTemplateSnapshot(provider, row.Name, row.ExternalTemplateId, row.Body);
    }

    private static IQueryable<SmsTemplate> AsNoTracking(DbSet<SmsTemplate> set)
    {
        // DbSet from IApplicationDbContext is a plain DbSet, so the caller's context tracking
        // cannot be turned off through the abstraction alone; callers register this in a scope
        // where the context is used for reads only on the send path. Queryable still applies the
        // AsNoTracking hint so a tracking context does not start tracking these rows.
        return set.AsNoTracking();
    }
}
