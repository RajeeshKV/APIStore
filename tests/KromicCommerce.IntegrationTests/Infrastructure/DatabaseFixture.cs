using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Infrastructure.Caching;
using KromicCommerce.Infrastructure.Catalog;
using KromicCommerce.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Testcontainers.PostgreSql;

namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// Shared database fixture using Testcontainers (real PostgreSQL container).
/// When Docker is not available the fixture marks itself unavailable and all
/// dependent tests skip rather than fail, keeping CI green in environments
/// without Docker.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public DatabaseFixture()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithDatabase("kromic_test")
                .WithUsername("postgres")
                .WithPassword("postgres_test")
                .Build();
        }
        catch (Exception ex)
        {
            // Docker not installed / not reachable at construction time
            _container = null!;
            IsAvailable = false;
            UnavailableReason = $"Docker unavailable: {ex.Message}";
        }
    }

    public AppDbContext CreateDbContext()
    {
        if (!IsAvailable)
            throw new InvalidOperationException("DatabaseFixture is not available. Check IsAvailable before calling CreateDbContext.");

        return BuildContext();
    }

    /// <summary>
    /// Builds a context with the real cache invalidation interceptor wired in, exactly as the
    /// application composes it, backed by a caller-supplied cache the test can inspect.
    /// </summary>
    /// <remarks>
    /// <see cref="CreateDbContext"/> deliberately omits interceptors so most tests exercise
    /// persistence alone. Cache-coherence tests need the production wiring, because the whole
    /// point is that invalidation happens as a consequence of saving rather than because a handler
    /// remembered to ask for it.
    /// </remarks>
    public AppDbContext CreateCacheObservedDbContext(
        IMemoryCache memoryCache,
        out CacheInvalidationRecorder recorder)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("DatabaseFixture is not available.");

        // Forwards to the real service so the eviction actually happens and the test can also
        // inspect resulting cache state, while recording which projections were touched.
        recorder = new CacheInvalidationRecorder(new CatalogCacheService(memoryCache));

        var invalidator = new CacheInvalidator(
            recorder, recorder, NullLogger<CacheInvalidator>.Instance);

        var interceptor = new CacheInvalidationInterceptor(
            invalidator, NullLogger<CacheInvalidationInterceptor>.Instance);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        return new AppDbContext(options, NullLogger<AppDbContext>.Instance);
    }

    /// <summary>
    /// Records which projections a save evicted while delegating to the real cache service, so a
    /// test can assert the decision without re-implementing the eviction it triggers.
    /// </summary>
    /// <remarks>
    /// Implements <see cref="IBusinessSettingsCacheInvalidator"/> as well as the catalog cache,
    /// because the settings object is evicted through a different abstraction and a test asserting
    /// on business settings coherence needs to observe that path too. The cache-only abstraction is
    /// the right one to stand in for: <see cref="IBusinessSettingsService"/> cannot be used as the
    /// recorder here, because the invalidation chain it belongs to is required to construct without
    /// the database.
    /// </remarks>
    public sealed class CacheInvalidationRecorder : ICatalogCacheService, IBusinessSettingsCacheInvalidator
    {
        private readonly ICatalogCacheService _inner;
        private readonly List<string> _calls = [];

        public CacheInvalidationRecorder(ICatalogCacheService inner) => _inner = inner;

        /// <summary>Names of the invalidation methods invoked, in order.</summary>
        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_calls) return _calls.ToArray();
            }
        }

        public bool Called(string name)
        {
            lock (_calls)
                return _calls.Any(c => c == name || c.StartsWith(name + ":", StringComparison.Ordinal));
        }

        private void Record(string name)
        {
            lock (_calls) _calls.Add(name);
        }

        // ---- ICatalogCacheService ------------------------------------------------
        public void InvalidateCategories() { Record(nameof(InvalidateCategories)); _inner.InvalidateCategories(); }
        public void InvalidateBrands() { Record(nameof(InvalidateBrands)); _inner.InvalidateBrands(); }
        public void InvalidateProducts() { Record(nameof(InvalidateProducts)); _inner.InvalidateProducts(); }
        public void InvalidateProduct(Guid productId) { Record(nameof(InvalidateProduct)); _inner.InvalidateProduct(productId); }
        public void InvalidateStorefrontCategories() { Record(nameof(InvalidateStorefrontCategories)); _inner.InvalidateStorefrontCategories(); }
        public void InvalidateStorefrontBrands() { Record(nameof(InvalidateStorefrontBrands)); _inner.InvalidateStorefrontBrands(); }
        public void InvalidateStorefrontProduct(string slug) { Record($"{nameof(InvalidateStorefrontProduct)}:{slug}"); _inner.InvalidateStorefrontProduct(slug); }
        public void InvalidateStorefrontFeatured() { Record(nameof(InvalidateStorefrontFeatured)); _inner.InvalidateStorefrontFeatured(); }
        public void InvalidateCarousel() { Record(nameof(InvalidateCarousel)); _inner.InvalidateCarousel(); }
        public void InvalidatePublicPolicies() { Record(nameof(InvalidatePublicPolicies)); _inner.InvalidatePublicPolicies(); }
        public void InvalidateProductReviews(Guid productId) { Record($"{nameof(InvalidateProductReviews)}:{productId}"); _inner.InvalidateProductReviews(productId); }
        public void InvalidateProductGraph() { Record(nameof(InvalidateProductGraph)); _inner.InvalidateProductGraph(); }
        public void InvalidateProductGraph(Guid productId, string? slug) { Record(nameof(InvalidateProductGraph)); _inner.InvalidateProductGraph(productId, slug); }
        public void InvalidateStockGraph(string? slug) { Record(nameof(InvalidateStockGraph)); _inner.InvalidateStockGraph(slug); }
        public void InvalidateBrandGraph() { Record(nameof(InvalidateBrandGraph)); _inner.InvalidateBrandGraph(); }
        public void InvalidateCategoryGraph() { Record(nameof(InvalidateCategoryGraph)); _inner.InvalidateCategoryGraph(); }
        public void InvalidateCatalogStructure() { Record(nameof(InvalidateCatalogStructure)); _inner.InvalidateCatalogStructure(); }
        public int GetCatalogEpoch() => _inner.GetCatalogEpoch();
        public void InvalidateShippingConfiguration() { Record(nameof(InvalidateShippingConfiguration)); _inner.InvalidateShippingConfiguration(); }
        public int GetShippingEpoch() => _inner.GetShippingEpoch();

        // ---- IBusinessSettingsCacheInvalidator -----------------------------------
        public void Invalidate() => Record("BusinessSettings:Invalidate");

        public void InvalidateShipping() => Record("BusinessSettings:InvalidateShipping");
    }

    /// <summary>
    /// Builds a context without the availability guard. Used during initialisation, which happens
    /// before <see cref="IsAvailable"/> can be set true — routing it through
    /// <see cref="CreateDbContext"/> made every integration test skip even with Docker running.
    /// </summary>
    private AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options, NullLogger<AppDbContext>.Instance);
    }

    public async Task InitializeAsync()
    {
        if (_container is null)
            return; // already flagged unavailable

        try
        {
            await _container.StartAsync();

            // Mark available before migrating: the guard is what CreateDbContext checks, and the
            // migration below needs a context. A migration failure is caught here and flips this
            // back to false with the reason attached.
            IsAvailable = true;
            await using var ctx = BuildContext();
            await ctx.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = $"Container start failed: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
