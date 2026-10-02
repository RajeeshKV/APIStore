using KromicCommerce.Application.Caching;

namespace KromicCommerce.Application.Abstractions.Catalog;

/// <summary>
/// Applies a <see cref="CacheInvalidationPlan"/> produced by
/// <see cref="CacheDependencyGraph"/> to the cache.
///
/// <para>
/// This is the single write path for cache eviction. Handlers do not assemble invalidation calls
/// by hand; the EF save interceptor derives the plan from the entities that changed and calls
/// this, so a mutation cannot bypass invalidation by forgetting to remember it.
/// </para>
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// Evicts every projection named by <paramref name="plan"/>. No-op for an empty plan.
    /// </summary>
    /// <remarks>
    /// Must only be called after the change is durable. Over-invalidating is always safe;
    /// invalidating before the write commits is not, because a concurrent read could repopulate
    /// the cache from the pre-commit state.
    /// </remarks>
    ValueTask ApplyAsync(CacheInvalidationPlan plan, CancellationToken cancellationToken = default);
}