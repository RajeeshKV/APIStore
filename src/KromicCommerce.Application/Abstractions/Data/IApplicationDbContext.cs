using KromicCommerce.Application.Abstractions.Security;
using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Domain.Outbox;
using KromicCommerce.Domain.Promotions;
using KromicCommerce.Domain.Store;
using KromicCommerce.Domain.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KromicCommerce.Application.Abstractions.Data;

public interface IApplicationDbContext
{
    /// <summary>Provides access to database-level operations (transactions, migrations, raw SQL).</summary>
    DatabaseFacade Database { get; }
    /// <summary>Access to the EF change tracker for cache clearing on concurrency retries.</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<ExternalLogin> ExternalLogins { get; }
    DbSet<OtpRequest> OtpRequests { get; }
    DbSet<CustomerProfile> CustomerProfiles { get; }
    DbSet<CustomerAddress> CustomerAddresses { get; }

    // -----------------------------------------------------------------------
    // Store
    // -----------------------------------------------------------------------
    DbSet<BusinessSettings> BusinessSettings { get; }
    DbSet<StorePolicy> StorePolicies { get; }

    // -----------------------------------------------------------------------
    // Catalog
    // -----------------------------------------------------------------------
    DbSet<Category> Categories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductImage> ProductImages { get; }
    DbSet<ProductAttribute> ProductAttributes { get; }
    DbSet<ProductAttributeValue> ProductAttributeValues { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<InventoryItem> InventoryItems { get; }

    // -----------------------------------------------------------------------
    // Cart
    // -----------------------------------------------------------------------
    DbSet<Cart> Carts { get; }
    DbSet<CartItem> CartItems { get; }

    // -----------------------------------------------------------------------
    // Orders & Payments
    // -----------------------------------------------------------------------
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Payment> Payments { get; }

    // -----------------------------------------------------------------------
    // Outbox & Webhooks
    // -----------------------------------------------------------------------
    DbSet<OutboxEvent> OutboxEvents { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }

    // -----------------------------------------------------------------------
    // Promotions
    // -----------------------------------------------------------------------
    DbSet<Promotion> Promotions { get; }
    DbSet<PromotionUsage> PromotionUsages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------
    // Cart atomic operations
    // Raw SQL is required for PostgreSQL ON CONFLICT upserts that prevent
    // lost updates and duplicate rows under high concurrency. These methods
    // live here so Application handlers stay free of relational EF extensions.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Atomically finds an existing active cart for the customer or creates one.
    /// Uses INSERT ... ON CONFLICT DO NOTHING to guarantee exactly one active
    /// cart per customer even when concurrent requests race through the
    /// "no cart found" branch simultaneously.
    /// Returns the Id of the surviving cart row.
    /// </summary>
    Task<Guid> FindOrCreateCustomerCartAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically finds an existing active cart for the anonymous session or creates one.
    /// Returns the Id of the surviving cart row and the anonymous session id used.
    /// </summary>
    Task<(Guid CartId, string AnonymousId)> FindOrCreateAnonymousCartAsync(
        string? existingAnonymousId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically upserts a cart item using INSERT ... ON CONFLICT DO UPDATE.
    /// If the item already exists the quantity is incremented atomically —
    /// no read-modify-write race, no lost updates.
    /// Relies on the unique index ix_cart_items_cart_product_variant (NULLS NOT DISTINCT).
    /// </summary>
    Task UpsertCartItemAsync(
        Guid cartId,
        Guid productId,
        Guid? variantId,
        int quantity,
        CancellationToken cancellationToken = default);
}
