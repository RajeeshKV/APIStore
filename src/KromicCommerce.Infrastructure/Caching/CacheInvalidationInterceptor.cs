using System.Collections.Concurrent;
using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Caching;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace KromicCommerce.Infrastructure.Caching;

/// <summary>
/// Derives cache invalidation from what a save actually changed, and applies it once the write is
/// durable. Registered as an EF Core save interceptor so that no code path — present or future —
/// can persist a change to a cached entity without evicting what that change dirties.
/// </summary>
/// <remarks>
/// <para>
/// This replaces "every handler must remember to invalidate", which is what let the graph drift:
/// the payment webhook released reserved stock without invalidating the stock graph, and admin
/// settings changes left cached product pages holding the old currency code. Both classes of bug
/// are now structurally impossible for change-tracked writes.
/// </para>
///
/// <para>
/// The plan is captured in <c>SavingChanges</c> and applied in <c>SavedChanges</c> rather than
/// doing all the work at either end. Before the save the write is not durable, so evicting then
/// would let a concurrent read repopulate the cache from the pre-commit state. After the save the
/// change tracker has already folded Added rows back to Unchanged and detached Deleted ones, so
/// the information needed to decide what to evict no longer exists.
/// </para>
///
/// <para>
/// Two escape hatches remain, and both are narrow. Raw SQL bypassing the change tracker (the
/// atomic <c>INSERT ... ON CONFLICT</c> helpers on <c>AppDbContext</c>) is invisible here, as are
/// writes made by another process. Those callers must invalidate explicitly.
/// </para>
/// </remarks>
internal sealed class CacheInvalidationInterceptor(
    ICacheInvalidator invalidator,
    ILogger<CacheInvalidationInterceptor> logger) : SaveChangesInterceptor
{
    /// <summary>
    /// Pending plans, keyed by context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed rather than held in a field because one interceptor instance is resolved per DI
    /// scope and a scope can own several contexts. Two contexts saving concurrently would otherwise
    /// overwrite each other's plan.
    /// </para>
    /// <para>
    /// A plan captured for a save that then throws is never applied, because the plan is only read
    /// in SavedChanges, which does not run on failure. The stale entry is simply overwritten by the
    /// next save on that context, so no cleanup hook is needed.
    /// </para>
    /// </remarks>
    private readonly ConcurrentDictionary<DbContext, PendingInvalidation> _pending = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            try
            {
                _pending[eventData.Context] = Capture(eventData.Context);
            }
            catch (Exception ex)
            {
                // Never let cache bookkeeping abort a business write. Falling back to the explicit
                // per-handler invalidations is strictly better than failing the save.
                _pending.TryRemove(eventData.Context, out _);
                logger.LogError(ex, "Could not derive a cache invalidation plan; save continues.");
            }
        }

        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null
            && result > 0
            && _pending.TryRemove(eventData.Context, out var pending))
        {
            var plan = await BuildPlanAsync(pending, eventData.Context, cancellationToken)
                .ConfigureAwait(false);

            if (!plan.IsEmpty)
                await invalidator.ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
        }
        else if (eventData.Context is not null)
        {
            _pending.TryRemove(eventData.Context, out _);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        // No handler uses the synchronous SaveChanges overload, and resolving product slugs needs a
        // query. Failing loudly is better than silently persisting without evicting if a
        // synchronous save is ever introduced.
        if (result > 0)
            throw new NotSupportedException(
                "Use SaveChangesAsync so cache invalidation can resolve affected products. " +
                "A synchronous save would persist changes without evicting stale entries.");

        return result;
    }

    // -----------------------------------------------------------------------
    // Capture (before the save, while the states are still meaningful)
    // -----------------------------------------------------------------------

private PendingInvalidation Capture(DbContext context)
    {
        var projections = CacheProjection.None;
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reviewProductIds = new HashSet<Guid>();
        var stockProductIds = new HashSet<Guid>();
        var currencyChanged = false;
        var deliveryChanged = false;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Owned graphs are tracked as separate entries but are not entities in their own right,
            // so they are handled through the entity that owns them rather than judged here.
            if (entry.Metadata.IsOwned())
                continue;

            var entity = entry.Entity;

            // BusinessSettings needs special handling before the state gate: EF marks the owned
            // entry Modified and leaves the principal Unchanged, so gating on the owner's own state
            // would skip exactly the changes that matter most — a Delivery or Auth edit.
            if (entity is BusinessSettings settings)
            {
                if (!BusinessSettingsChanged(context, entry, settings))
                    continue;

                projections |= CacheDependencyGraph.For(typeof(BusinessSettings));

                if (entry.State is EntityState.Added
                    || ChangedPropertyNames(entry).Contains(nameof(BusinessSettings.CurrencyCode)))
                    currencyChanged = true;

                deliveryChanged |= OwnedValueChanged(context, settings.Delivery);
                continue;
            }

            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            projections |= CacheDependencyGraph.For(entity.GetType());

            switch (entity)
            {
                case Product product:
                    slugs.Add(product.Slug);
                    break;

                case ProductReview review:
                    reviewProductIds.Add(review.ProductId);
                    break;

                case InventoryItem inventory:
                    // Stock carries a product id, but the cached product page is keyed by slug.
                    stockProductIds.Add(inventory.ProductId);
                    break;
            }
        }

        return new PendingInvalidation(
            projections, slugs, reviewProductIds, stockProductIds, currencyChanged, deliveryChanged);
    }

    /// <summary>
    /// True when the settings row or any of its owned configuration graphs changed.
    /// </summary>
    /// <remarks>
    /// Every owned graph is listed explicitly. That is deliberate: the cached BusinessSettings
    /// entry is the whole entity, including the encrypted Google and Razorpay credentials, so an
    /// Auth or Payment edit must evict it just as a locale edit must. Relying on the owner's state
    /// would miss all of them, because EF never marks the principal Modified for an owned change.
    /// </remarks>
    private static bool BusinessSettingsChanged(
        DbContext context, EntityEntry entry, BusinessSettings settings) =>
        EntryChanged(entry)
        || OwnedValueChanged(context, settings.Delivery)
        || OwnedValueChanged(context, settings.Auth)
        || OwnedValueChanged(context, settings.Email)
        || OwnedValueChanged(context, settings.Seo)
        || OwnedValueChanged(context, settings.Tax)
        || OwnedValueChanged(context, settings.Payment);

    private static HashSet<string> ChangedPropertyNames(EntityEntry entry) =>
        entry.Properties
            .Where(p => p.IsModified)
            .Select(p => p.Metadata.Name)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// True when an owned navigation changed in any way EF can express: properties modified in
    /// place, or the owned instance replaced outright (which detaches the old one and attaches a
    /// new entry in the Added state rather than marking properties modified).
    /// </summary>
    /// <remarks>
    /// Delivery is mapped as an owned navigation, so its changes are reported on the owned entry
    /// rather than on the owner. Checking only the owner's scalar properties would silently miss a
    /// shipping change — the exact bug this layer exists to prevent.
    ///
    /// <para>
    /// Owned navigations are resolved through the change tracker by instance identity, which keeps
    /// this working whether EF reports owned types as references or as owned navigations.
    /// </para>
    /// <remarks>
    /// <summary>
    /// True when a tracked value object owned by <paramref name="owner"/> changed in any way EF can
    /// express: properties modified in place, or the owned instance replaced outright (which
    /// attaches a new entry in the Added state rather than marking properties modified).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Delivery is mapped as an owned navigation, so its changes are reported on a separate change
    /// tracker entry rather than on the BusinessSettings row. Checking only the owner's scalar
    /// properties would silently miss a shipping change — the exact bug this layer exists to
    /// prevent.
    /// </para>
    /// <para>
    /// The owned entry is located by instance identity rather than through the navigation API,
    /// which keeps this working whether EF reports owned types as references or as owned
    /// navigations.
    /// </para>
    /// </remarks>
    private static bool OwnedValueChanged(DbContext context, object? ownedValue)
    {
        if (ownedValue is null)
            return false;

        var ownedEntry = context.ChangeTracker.Entries()
            .FirstOrDefault(e => ReferenceEquals(e.Entity, ownedValue));

        return ownedEntry is not null && EntryChanged(ownedEntry);
    }

    private static bool EntryChanged(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
        || entry.Properties.Any(p => p.IsModified);

    // -----------------------------------------------------------------------
    // Plan (after the save, when the write is durable)
    // -----------------------------------------------------------------------

    private async Task<CacheInvalidationPlan> BuildPlanAsync(
        PendingInvalidation pending, DbContext context, CancellationToken cancellationToken)
    {
        var builder = new CacheInvalidationPlanBuilder
        {
            Seed = pending.Projections | RefinedProjections(pending),
        };

        foreach (var slug in pending.Slugs)
            builder.AddSlug(slug);

        foreach (var productId in pending.ReviewProductIds)
            builder.AddProductId(productId);

        // One indexed lookup per save, far cheaper than orphaning every cached product page
        // through the catalog epoch.
        if (pending.StockProductIds.Count > 0)
        {
            var stockSlugs = await context.Set<Product>()
                .AsNoTracking()
                .Where(p => pending.StockProductIds.Contains(p.Id))
                .Select(p => p.Slug)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var slug in stockSlugs)
                builder.AddSlug(slug);
        }

        return builder.Build();
    }

    private static CacheProjection RefinedProjections(PendingInvalidation pending)
    {
        var refined = CacheProjection.None;

        // Every storefront product page renders the currency code from BusinessSettings.
        if (pending.CurrencyChanged)
            refined |= CacheProjection.AllStorefrontProducts;

        if (pending.DeliveryChanged)
            refined |= CacheProjection.ShippingConfiguration;

        return refined;
    }

    private sealed record PendingInvalidation(
        CacheProjection Projections,
        HashSet<string> Slugs,
        HashSet<Guid> ReviewProductIds,
        HashSet<Guid> StockProductIds,
        bool CurrencyChanged,
        bool DeliveryChanged);
}