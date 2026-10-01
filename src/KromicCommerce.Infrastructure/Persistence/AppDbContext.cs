using KromicCommerce.Application.Abstractions.Data;
using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Domain.Outbox;
using KromicCommerce.Domain.Promotions;
using KromicCommerce.Domain.Sms;
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
    // SMS
    // -----------------------------------------------------------------------
    public DbSet<SmsTemplate> SmsTemplates => Set<SmsTemplate>();
    public DbSet<SmsProviderConfig> SmsProviderConfigs => Set<SmsProviderConfig>();
    public DbSet<OtpSendClaim> OtpSendClaims => Set<OtpSendClaim>();

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

    // -----------------------------------------------------------------------
    // Cart atomic operations — implement IApplicationDbContext members
    // Raw SQL keeps PostgreSQL ON CONFLICT semantics that EF Core cannot express.
    // -----------------------------------------------------------------------

    public async Task<Guid> FindOrCreateCustomerCartAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        // Look for an existing active cart first (no lock needed on the read)
        var existingId = await Carts
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.ExpiresAt > DateTime.UtcNow)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingId.HasValue) return existingId.Value;

        // Create one atomically. ON CONFLICT DO NOTHING means if a concurrent
        // request already inserted a row, this is a no-op and we SELECT theirs.
        // The ix_carts_customer partial unique index enforces one row per customer.
        var cartId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(30);

        await Database.ExecuteSqlRawAsync(
            @"INSERT INTO carts (""Id"", ""CustomerId"", ""AnonymousId"", ""ExpiresAt"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
              VALUES ({0}, {1}, NULL, {2}, {3}, {3})
              ON CONFLICT DO NOTHING",
            new object[] { cartId, customerId, expiresAt, now },
            cancellationToken);

        return await Carts
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId && c.ExpiresAt > DateTime.UtcNow)
            .Select(c => c.Id)
            .FirstAsync(cancellationToken);
    }

    public async Task<(Guid CartId, string AnonymousId)> FindOrCreateAnonymousCartAsync(
        string? existingAnonymousId,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(existingAnonymousId))
        {
            var existingId = await Carts
                .AsNoTracking()
                .Where(c => c.AnonymousId == existingAnonymousId && c.ExpiresAt > DateTime.UtcNow)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingId.HasValue) return (existingId.Value, existingAnonymousId);
        }

        // Generate a new anonymous id and create the cart atomically
        var newAnonId = GenerateAnonymousCartId();
        var cartId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(7);

        await Database.ExecuteSqlRawAsync(
            @"INSERT INTO carts (""Id"", ""CustomerId"", ""AnonymousId"", ""ExpiresAt"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
              VALUES ({0}, NULL, {1}, {2}, {3}, {3})
              ON CONFLICT DO NOTHING",
            new object[] { cartId, newAnonId, expiresAt, now },
            cancellationToken);

        var survivingId = await Carts
            .AsNoTracking()
            .Where(c => c.AnonymousId == newAnonId && c.ExpiresAt > DateTime.UtcNow)
            .Select(c => c.Id)
            .FirstAsync(cancellationToken);

        return (survivingId, newAnonId);
    }

    public async Task UpsertCartItemAsync(
        Guid cartId,
        Guid productId,
        Guid? variantId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var itemId = Guid.NewGuid();
        var addedAt = DateTime.UtcNow;

        if (variantId.HasValue)
        {
            await Database.ExecuteSqlRawAsync(
                @"INSERT INTO cart_items (""Id"", ""CartId"", ""ProductId"", ""VariantId"", ""Quantity"", ""AddedAt"")
                  VALUES ({0}, {1}, {2}, {3}, {4}, {5})
                  ON CONFLICT (""CartId"", ""ProductId"", ""VariantId"") WHERE ""VariantId"" IS NOT NULL
                  DO UPDATE SET ""Quantity"" = cart_items.""Quantity"" + EXCLUDED.""Quantity""",
                new object[] { itemId, cartId, productId, variantId.Value, quantity, addedAt },
                cancellationToken);
        }
        else
        {
            await Database.ExecuteSqlRawAsync(
                @"INSERT INTO cart_items (""Id"", ""CartId"", ""ProductId"", ""VariantId"", ""Quantity"", ""AddedAt"")
                  VALUES ({0}, {1}, {2}, NULL, {3}, {4})
                  ON CONFLICT (""CartId"", ""ProductId"", ""VariantId"")
                  DO UPDATE SET ""Quantity"" = cart_items.""Quantity"" + EXCLUDED.""Quantity""",
                new object[] { itemId, cartId, productId, quantity, addedAt },
                cancellationToken);
        }
    }

    public async Task<bool> TryAcquireOtpSendClaimAsync(
        string phoneNumber,
        OtpPurpose purpose,
        DateTime staleBefore,
        CancellationToken cancellationToken = default)
    {
        // Reclaim a claim whose owner died mid-send. Without this a crash during delivery
        // would block resends for the whole staleness window.
        await Database.ExecuteSqlRawAsync(
            @"DELETE FROM otp_send_claims
              WHERE ""PhoneNumber"" = {0} AND ""Purpose"" = {1} AND ""CreatedAt"" < {2}",
            new object[] { phoneNumber, purpose.ToString(), staleBefore },
            cancellationToken);

        // ON CONFLICT DO NOTHING — the unique index ix_otp_send_claims_phone_purpose decides
        // the winner. Both concurrent requests reach this INSERT; exactly one inserts, and the
        // other is told it lost without an exception to unwind.
        var affected = await Database.ExecuteSqlRawAsync(
            @"INSERT INTO otp_send_claims (""Id"", ""PhoneNumber"", ""Purpose"", ""CreatedAt"")
              VALUES ({0}, {1}, {2}, {3})
              ON CONFLICT DO NOTHING",
            new object[] { Guid.NewGuid(), phoneNumber, purpose.ToString(), DateTime.UtcNow },
            cancellationToken);

        return affected == 1;
    }

    public async Task ReleaseOtpSendClaimAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default)
        => await Database.ExecuteSqlRawAsync(
            @"DELETE FROM otp_send_claims
              WHERE ""PhoneNumber"" = {0} AND ""Purpose"" = {1}",
            new object[] { phoneNumber, purpose.ToString() },
            cancellationToken);

    private static string GenerateAnonymousCartId()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
