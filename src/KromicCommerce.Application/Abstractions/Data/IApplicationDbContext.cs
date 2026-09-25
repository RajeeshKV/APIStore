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
}
