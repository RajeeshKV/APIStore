namespace KromicCommerce.Application.Caching;

/// <summary>
/// The complete set of projections a single logical change dirties, together with the identities
/// needed to narrow parameterised projections down to the rows actually affected.
/// </summary>
/// <remarks>
/// Produced by <see cref="CacheDependencyGraph"/> from the entities a save touched, and applied by
/// <c>ICacheInvalidator</c>. Handlers do not build these by hand; doing so is what allowed the
/// invalidation graph to drift out of sync with the cached projections in the first place.
/// </remarks>
public sealed record CacheInvalidationPlan
{
    public static readonly CacheInvalidationPlan Empty = new();

    public CacheInvalidationPlan(
        CacheProjection projections = CacheProjection.None,
        IEnumerable<string>? productSlugs = null,
        IEnumerable<Guid>? productIds = null)
    {
        Projections = projections;

        // Normalised here rather than at the call site so that no caller can build a plan that
        // evicts an empty cache key or the same slug twice.
        ProductSlugs = productSlugs is null
            ? []
            : productSlugs
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        ProductIds = productIds is null ? [] : productIds.Distinct().ToArray();
    }

    public CacheProjection Projections { get; init; }

    /// <summary>Slugs whose cached product pages are stale. Non-blank, lower-cased, de-duplicated.</summary>
    public IReadOnlyCollection<string> ProductSlugs { get; init; }

    /// <summary>Product ids whose cached review pages are stale.</summary>
    public IReadOnlyCollection<Guid> ProductIds { get; init; }

    public bool IsEmpty =>
        Projections == CacheProjection.None && ProductSlugs.Count == 0 && ProductIds.Count == 0;

    public override string ToString() =>
        IsEmpty
            ? "CacheInvalidationPlan(empty)"
            : $"CacheInvalidationPlan({Projections}, slugs: {ProductSlugs.Count}, products: {ProductIds.Count})";
}

/// <summary>
/// Accumulates a <see cref="CacheInvalidationPlan"/> while a save is being inspected.
/// </summary>
/// <remarks>
/// Normalisation and de-duplication deliberately happen in the plan constructor, so there is one
/// implementation of those rules rather than two that can drift.
/// </remarks>
public sealed class CacheInvalidationPlanBuilder
{
    private readonly HashSet<string> _slugs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _productIds = [];

    /// <summary>Starting projection set, typically taken from the dependency graph.</summary>
    public CacheProjection Seed { get; set; }

    public void Add(CacheProjection projection) => Seed |= projection;

    public void AddSlug(string? slug)
    {
        if (!string.IsNullOrWhiteSpace(slug))
            _slugs.Add(slug);
    }

    public void AddProductId(Guid productId) => _productIds.Add(productId);

    public bool IsEmpty =>
        Seed == CacheProjection.None && _slugs.Count == 0 && _productIds.Count == 0;

    public CacheInvalidationPlan Build() =>
        IsEmpty ? CacheInvalidationPlan.Empty : new CacheInvalidationPlan(Seed, _slugs, _productIds);
}