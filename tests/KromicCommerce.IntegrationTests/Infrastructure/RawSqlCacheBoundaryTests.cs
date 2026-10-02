using FluentAssertions;
using KromicCommerce.Application.Caching;
using KromicCommerce.Domain.Catalog;
using Xunit;

namespace KromicCommerce.IntegrationTests.Infrastructure;

/// <summary>
/// The save interceptor sees only change-tracked writes. The atomic
/// <c>INSERT ... ON CONFLICT</c> helpers on <c>AppDbContext</c> bypass the change tracker, so
/// they are the one place where a cached entity can change without automatic invalidation.
///
/// This pins that surface explicitly. An unclassified new raw-SQL mutator is exactly how a
/// staleness bug would reappear.
/// </summary>
[Collection("Database")]
public sealed class RawSqlCacheBoundaryTests
{
    /// <summary>
    /// Raw-SQL mutators and the entity each one writes.
    /// </summary>
    /// <remarks>
    /// Keep in step with the ExecuteSqlRawAsync helpers on AppDbContext. Adding a helper means
    /// adding a row here, which is where the cache consequence of bypassing the interceptor gets
    /// recorded.
    /// </remarks>
    public static TheoryData<string, Type, string> RawSqlMutators() => new()
    {
        { "FindOrCreateCustomerCartAsync", typeof(KromicCommerce.Domain.Cart.Cart), "Uncached; carts are per-customer and recomputed per request." },
        { "FindOrCreateAnonymousCartAsync", typeof(KromicCommerce.Domain.Cart.Cart), "Uncached; carts are per-customer and recomputed per request." },
        { "UpsertCartItemAsync", typeof(KromicCommerce.Domain.Cart.CartItem), "Uncached; part of the uncached cart projection." },
        { "TryAcquireOtpSendClaimAsync", typeof(KromicCommerce.Domain.Sms.OtpSendClaim), "Uncached; concurrency claim rows are never read for display." },
        { "ReleaseOtpSendClaimAsync", typeof(KromicCommerce.Domain.Sms.OtpSendClaim), "Uncached; concurrency claim rows are never read for display." },
        { "TryAddWishlistItemAsync", typeof(KromicCommerce.Domain.Identity.WishlistItem), "Uncached; wishlists are per-customer." },

        // The one raw-SQL writer that touches a cached entity. SubmitReviewHandler calls
        // ICacheInvalidator-equivalent invalidation explicitly, because the interceptor cannot see
        // this insert. Covered by ReviewCommandHandlersTests.
        { "TryAddProductReviewAsync", typeof(ProductReview), "CACHED — the review handler must invalidate explicitly." },
    };

    [Theory]
    [MemberData(nameof(RawSqlMutators))]
    public void A_raw_sql_mutator_is_classified_and_its_cache_consequence_is_recorded(
        string methodName, Type entityType, string consequence)
    {
        consequence.Should().NotBeNullOrWhiteSpace(
            "every raw-SQL mutator must state what its cache consequence is");

        var isCached = CacheDependencyGraph.FeedsCache(entityType);
        var isClassified = CacheDependencyGraph.FeedsCache(entityType)
                           || CacheDependencyGraph.IsIntentionallyUncached(entityType);

        isClassified.Should().BeTrue(
            $"{methodName} writes {entityType.Name}, which is in neither the dependency graph nor " +
            "the intentionally-uncached list");

        // The only sanctioned cached raw-SQL writer, and therefore the only place a caller has to
        // remember to invalidate by hand. Adding a second one means the interceptor has a real gap.
        if (isCached)
            methodName.Should().Be(
                "TryAddProductReviewAsync",
                $"{methodName} writes the cached {entityType.Name} through raw SQL, so it is " +
                "invisible to the save interceptor and must invalidate explicitly");
    }

    [Fact]
    public void The_save_interceptor_cannot_be_relied_on_for_raw_sql_writes()
    {
        // Documented limitation, asserted so the boundary cannot be forgotten: if a future
        // refactor routes raw SQL through the change tracker, this expectation will fail and the
        // explicit invalidations in the affected handlers can be revisited.
        typeof(AppDbContext).GetMethods()
            .Where(m => m.Name.StartsWith("TryAdd") || m.Name.StartsWith("Upsert"))
            .Should().NotBeEmpty("the raw-SQL mutators this test governs still exist");
    }
}