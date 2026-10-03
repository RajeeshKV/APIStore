using System.Reflection;
using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Infrastructure.Caching;
using KromicCommerce.Infrastructure.DependencyInjection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// Proves the production DI graph is acyclic where it matters most.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppDbContext"/> is constructed with a <see cref="CacheInvalidationInterceptor"/>
/// resolved from the same scope, so resolving the context requires <see cref="ICacheInvalidator"/>.
/// While the cycle <c>AppDbContext → CacheInvalidationInterceptor → CacheInvalidator →
/// BusinessSettingsService → AppDbContext</c> was in place, the container did not throw when
/// resolving it — it blocked, because the re-entrant resolution happened inside a factory delegate
/// that the container's own call-site lock already held. In production that surfaced as a deploy
/// that started, logged "Checking for pending EF Core migrations...", and then timed out with no
/// open port, never naming a DI problem.
/// </para>
///
/// <para>
/// These tests therefore resolve through the real registration path
/// (<see cref="InfrastructureServiceExtensions.AddInfrastructureServices"/> — the same call
/// <c>Program.cs</c> makes) rather than hand-wiring mocks, and every resolution runs on a bounded
/// wait so a reintroduced cycle fails the test instead of hanging the whole run.
/// </para>
///
/// <para>
/// No database is contacted: EF Core resolves a <c>DbContext</c> without connecting, and nothing
/// here executes a query.
/// </para>
///
/// <para>
/// <c>ValidateOnBuild</c> is deliberately off. Eagerly constructing every registration walks the
/// MVC services too, and those need a real web host (<c>IWebHostEnvironment</c>,
/// <c>EndpointDataSource</c>); failing on them would say nothing about this graph.
/// <c>ValidateScopes</c> is on, because a scoped service captured by a singleton is the usual way
/// a scope cycle stays hidden until runtime.
/// </para>
/// </remarks>
public sealed class DependencyInjectionGraphTests
{
    /// <summary>
    /// Generous next to a resolution that normally takes single-digit milliseconds, and short next
    /// to a test run: the only expected cost is EF building its internal service provider.
    /// </summary>
    private static readonly TimeSpan ResolutionTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void AppDbContext_resolves_from_a_scope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Resolve(() => scope.ServiceProvider.GetRequiredService<AppDbContext>())
            .Should().NotBeNull();
    }

    [Fact]
    public void The_cache_invalidation_chain_resolves()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Resolve(() => scope.ServiceProvider.GetRequiredService<ICacheInvalidator>())
            .Should().NotBeNull();

        Resolve(() => scope.ServiceProvider.GetRequiredService<CacheInvalidationInterceptor>())
            .Should().NotBeNull();
    }

    /// <summary>
    /// The interceptor must stay wired to the context. A fix that dropped
    /// <c>AddInterceptors</c> would make the tests above pass while silently disabling cache
    /// invalidation, so the attachment is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void AppDbContext_is_registered_with_the_cache_invalidation_interceptor()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var db = Resolve(() => scope.ServiceProvider.GetRequiredService<AppDbContext>());

        FindInterceptors(db).Should().ContainSingle()
            .Which.Should().BeOfType<CacheInvalidationInterceptor>();
    }

    [Fact]
    public void BusinessSettings_still_resolves_against_AppDbContext()
    {
        // The other half of the graph is deliberately unchanged: settings read and write through
        // the context, and the cache invalidation sits below them, never above.
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Resolve(() => scope.ServiceProvider.GetRequiredService<IBusinessSettingsService>())
            .Should().NotBeNull();
    }

    /// <summary>
    /// The settings cache invalidation the interceptor relies on must itself resolve, otherwise the
    /// cycle would only have moved rather than been removed.
    /// </summary>
    [Fact]
    public void The_business_settings_cache_invalidator_resolves_on_its_own()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Resolve(
                () => scope.ServiceProvider.GetRequiredService<IBusinessSettingsCacheInvalidator>())
            .Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolves a service on a background task with a deadline, and rethrows whatever it threw.
    /// </summary>
    /// <remarks>
    /// The deadline is the point of the helper, not a timeout convenience. A circular dependency in
    /// this graph does not surface as an exception — the container blocks — so a plain
    /// <c>GetRequiredService</c> call would hang the test run indefinitely instead of reporting the
    /// regression. Resolution stays on the caller's scope: the cycle only reproduces within a single
    /// scope, so a fresh one would hide it. The abandoned task is left to die with the process.
    /// </remarks>
    private static T Resolve<T>(Func<T> resolve)
    {
        var task = Task.Run(resolve);

        if (!task.Wait(ResolutionTimeout))
            throw new TimeoutException(
                $"Resolving {typeof(T).Name} blocked for {ResolutionTimeout}. " +
                "The dependency graph has almost certainly become circular: AppDbContext is " +
                "constructed with the cache invalidation interceptor, so nothing that interceptor " +
                "requires may reach AppDbContext again.");

        // GetAwaiter().Result rather than .Result so a resolution failure surfaces as the original
        // exception instead of an AggregateException wrapper.
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Reads the interceptors EF actually attached to a resolved context.
    /// </summary>
    /// <remarks>
    /// <c>AddInterceptors</c> stores the interceptor in <c>CoreOptionsExtension</c>, which is
    /// reachable from the public <see cref="DbContextOptions"/> surface through
    /// <c>FindExtension</c>. Reading the list reflectively keeps the assertion off EF internals
    /// while still failing loudly if a future EF moves it.
    /// </remarks>
    private static IReadOnlyList<object> FindInterceptors(AppDbContext db)
    {
        var core = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();
        core.Should().NotBeNull("AddInterceptors stores the interceptor in CoreOptionsExtension.");

        var property = typeof(CoreOptionsExtension)
            .GetProperty("Interceptors", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        property.Should().NotBeNull("CoreOptionsExtension must expose the interceptor list.");

        var interceptors = property.GetValue(core);
        interceptors.Should().NotBeNull();

        return ((System.Collections.IEnumerable)interceptors!)
            .Cast<object>()
            .ToList();
    }

    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddSingleton<IConfiguration>(TestConfiguration())
            .AddInfrastructureServices(TestConfiguration())
            .AddLogging()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    /// <summary>
    /// The minimum configuration <c>AddInfrastructureServices</c> insists on at registration time.
    /// <c>Database</c> and <c>Jwt</c> are read eagerly and throw when absent; the rest only fail at
    /// host start through <c>ValidateOnStart</c>, which these tests never reach.
    /// </summary>
    private static IConfiguration TestConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] =
                    "Host=localhost;Port=5432;Database=kromic_di_graph;Username=test;Password=test",
                ["Jwt:SigningKey"] = new string('k', 48),
                ["Jwt:Issuer"] = "kromic-tests",
                ["Jwt:Audience"] = "kromic-tests"
            })
            .Build();
}
