using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Domain.Outbox;
using KromicCommerce.Domain.Promotions;
using KromicCommerce.Domain.Webhooks;
using KromicCommerce.Infrastructure.Persistence.Converters;

namespace KromicCommerce.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core database context.
/// Implements IApplicationDbContext so Application handlers can use it via interface only.
/// </summary>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ILogger<AppDbContext> logger)
    : DbContext(options), IApplicationDbContext
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<OtpRequest> OtpRequests => Set<OtpRequest>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    // -----------------------------------------------------------------------
    // Store
    // -----------------------------------------------------------------------
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();
    public DbSet<StorePolicy> StorePolicies => Set<StorePolicy>();

    // -----------------------------------------------------------------------
    // Catalog
    // -----------------------------------------------------------------------
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    // -----------------------------------------------------------------------
    // Cart
    // -----------------------------------------------------------------------
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    // -----------------------------------------------------------------------
    // Orders & Payments
    // -----------------------------------------------------------------------
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();

    // -----------------------------------------------------------------------
    // Outbox & Webhooks
    // -----------------------------------------------------------------------
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    // -----------------------------------------------------------------------
    // Promotions
    // -----------------------------------------------------------------------
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionUsage> PromotionUsages => Set<PromotionUsage>();

    // -----------------------------------------------------------------------
    // EF configuration
    // -----------------------------------------------------------------------
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // DomainEvent is not a persistent entity — ignore it globally
        modelBuilder.Ignore<DomainEvent>();
        // ShippingAddress is owned by Order — EF will find it via OwnsOne
        // OutboxEvent/WebhookEvent are root entities, not domain events

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Persist all enums as strings globally
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
        // Ensure DateTime is always UTC
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetAuditFields();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict detected during SaveChanges");
            throw;
        }
    }

    private void SetAuditFields()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.UpdatedAtUtc = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    break;
            }
        }
    }
}
