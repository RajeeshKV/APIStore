using KromicCommerce.Application.Abstractions.Security;
using KromicCommerce.Domain.Cart;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Orders;
using KromicCommerce.Domain.Outbox;
using KromicCommerce.Domain.Promotions;
using KromicCommerce.Domain.Sms;
using KromicCommerce.Domain.Store;
using KromicCommerce.Domain.Support;
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
    DbSet<WishlistItem> WishlistItems { get; }

    // -----------------------------------------------------------------------
    // Store
    // -----------------------------------------------------------------------
    DbSet<BusinessSettings> BusinessSettings { get; }
    DbSet<StorePolicy> StorePolicies { get; }

    // -----------------------------------------------------------------------
    // SMS
    // -----------------------------------------------------------------------
    DbSet<SmsProviderConfig> SmsProviderConfigs { get; }
    DbSet<OtpSendClaim> OtpSendClaims { get; }

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
    DbSet<CarouselSlide> CarouselSlides { get; }
    DbSet<ProductReview> ProductReviews { get; }
    DbSet<ReviewHelpfulVote> ReviewHelpfulVotes { get; }

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

    // -----------------------------------------------------------------------
    // Support
    // -----------------------------------------------------------------------
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketComment> TicketComments { get; }
    DbSet<TicketAttachment> TicketAttachments { get; }
    DbSet<TicketStatusHistory> TicketStatusHistory { get; }
    DbSet<TicketInvoice> TicketInvoices { get; }
    DbSet<InvoiceTemplate> InvoiceTemplates { get; }
    DbSet<SupportSettings> SupportSettings { get; }

    /// <summary>Captured marketing leads. Written by a public endpoint, read by admin only.</summary>
    DbSet<Lead> Leads { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------
    // Human-readable reference numbers
    //
    // Ticket and invoice numbers are quoted by customers in email support and printed on
    // documents that may be filed for accounting, so they have to be unique and they have
    // to stay unique under concurrency. A handler that counted today's rows to derive the
    // next number would race: two simultaneous creates both read count N and both try N+1.
    //
    // These methods call a PostgreSQL sequence, which is the only allocator that can hand out
    // a gap-free-until-exhaustion value without an application-level lock. Ids remain GUIDs
    // everywhere else — only the externally quoted reference is sequential.
    // -----------------------------------------------------------------------

    /// <summary>Next value of the shared ticket/invoice reference sequence.</summary>
    Task<long> NextTicketReferenceSequenceAsync(CancellationToken cancellationToken = default);

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
        Guid? productVariantId,
        int quantity,
        CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------
    // OTP send claim
    // -----------------------------------------------------------------------
    // The resend cooldown is enforced by reading the previous OTP row, and that
    // read-then-write sequence was not concurrency-safe: two simultaneous requests could
    // both observe "no recent code", both send an SMS, and both write a row. An in-memory
    // lock cannot help because the API may run as several instances.
    //
    // These methods use INSERT ... ON CONFLICT DO NOTHING, so the UNIQUE index on
    // (PhoneNumber, Purpose) decides the winner. Application checks cannot: any read a
    // request performs can be overtaken between the read and the write.

    /// <summary>
    /// Atomically takes the send claim for a phone number and purpose.
    /// Returns <c>true</c> when this caller owns the send and <c>false</c> when another
    /// request for the same number and purpose already holds it.
    /// </summary>
    /// <param name="staleBefore">
    /// Claims created before this instant are treated as abandoned (their owner died
    /// mid-send) and may be taken over. Must exceed the worst-case gateway send duration.
    /// </param>
    Task<bool> TryAcquireOtpSendClaimAsync(
        string phoneNumber,
        OtpPurpose purpose,
        DateTime staleBefore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the send claim. Idempotent, and must be called from a finally block so a
    /// failed or cancelled request does not block subsequent sends until the claim goes stale.
    /// </summary>
    Task ReleaseOtpSendClaimAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken = default);

    // -----------------------------------------------------------------------
    // Wishlist / reviews
    // -----------------------------------------------------------------------
    // Both rely on unique indexes created with NULLS NOT DISTINCT, so a NULL
    // ProductVariantId is a single key. A read-then-write sequence in a handler would lose the
    // race: two concurrent requests could both see "no existing row" and both insert. Letting the
    // index decide via ON CONFLICT DO NOTHING makes the loser wait, then observe the winner's row.

    /// <summary>
    /// Atomically inserts a wishlist item.
    /// Returns <c>true</c> when this caller created the row, <c>false</c> when the entry already
    /// existed. Making the add idempotent is deliberate: a double-tapped heart is a UI accident,
    /// not a conflict, and the storefront should not need a disable-and-retry dance.
    /// </summary>
    Task<bool> TryAddWishlistItemAsync(
        Guid customerId,
        Guid productId,
        Guid? productVariantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts a product review.
    /// Returns <c>true</c> when this caller created the row, <c>false</c> when the customer
    /// already has a review for that product/variant. Callers translate <c>false</c> into a
    /// conflict — unlike the wishlist, a second review is a genuine error, not a retry.
    /// </summary>
    Task<bool> TryAddProductReviewAsync(
        Guid customerId,
        Guid productId,
        Guid? productVariantId,
        int rating,
        string? title,
        string body,
        bool isVerifiedPurchase,
        ReviewStatus status,
        CancellationToken cancellationToken = default);
}
