namespace KromicCommerce.Domain.Promotions;

/// <summary>
/// Promotion aggregate root.
///
/// A promotion is defined by an admin and applied at checkout via a coupon code.
/// All eligibility rules are enforced here — never in controllers or thin services.
///
/// Coupon codes are stored normalised (uppercase, trimmed) so case-insensitive matching
/// is handled transparently.
///
/// Concurrency: PromotionUsage rows carry the per-customer and global usage counts.
/// Atomic usage reservation is done at the database level using a row-level lock
/// in the checkout transaction.
///
/// Financial rules:
///   - Percentage discount: 0 &lt; value &lt;= 100
///   - Fixed discount: value > 0, capped at the eligible subtotal (never negative total)
///   - MaxDiscountAmount optionally caps percentage discounts
/// </summary>
public sealed class Promotion : AuditableEntity
{
    private Promotion() { } // EF constructor

    public static Promotion Create(
        string name,
        string? description,
        string couponCode,
        DiscountType discountType,
        decimal discountValue,
        decimal? maxDiscountAmount,
        decimal? minimumOrderAmount,
        int? usageLimit,
        int? perCustomerUsageLimit,
        DateTime? startsAt,
        DateTime? expiresAt,
        PromotionApplicabilityType applicability,
        bool isFirstOrderOnly)
    {
        ValidateName(name);
        ValidateCouponCode(couponCode);
        ValidateDiscountValue(discountType, discountValue);
        ValidateDates(startsAt, expiresAt);

        if (maxDiscountAmount is < 0m)
            throw new ArgumentException("Max discount amount must be >= 0.", nameof(maxDiscountAmount));
        if (minimumOrderAmount is < 0m)
            throw new ArgumentException("Minimum order amount must be >= 0.", nameof(minimumOrderAmount));
        if (usageLimit is < 1)
            throw new ArgumentException("Usage limit must be >= 1 when specified.", nameof(usageLimit));
        if (perCustomerUsageLimit is < 1)
            throw new ArgumentException("Per-customer usage limit must be >= 1 when specified.", nameof(perCustomerUsageLimit));

        return new Promotion
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            CouponCode = NormaliseCouponCode(couponCode),
            DiscountType = discountType,
            DiscountValue = discountValue,
            MaxDiscountAmount = maxDiscountAmount,
            MinimumOrderAmount = minimumOrderAmount,
            UsageLimit = usageLimit,
            PerCustomerUsageLimit = perCustomerUsageLimit,
            StartsAt = startsAt,
            ExpiresAt = expiresAt,
            Applicability = applicability,
            IsFirstOrderOnly = isFirstOrderOnly,
            IsActive = false, // inactive until explicitly activated
            UsageCount = 0
        };
    }

    // -----------------------------------------------------------------------
    // Core fields
    // -----------------------------------------------------------------------
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Normalised (uppercase, trimmed) coupon code. Unique across all promotions.</summary>
    public string CouponCode { get; private set; } = string.Empty;

    public DiscountType DiscountType { get; private set; }

    /// <summary>
    /// The discount value.
    /// For Percentage: 0 &lt; value &lt;= 100.
    /// For FixedAmount: value > 0.
    /// </summary>
    public decimal DiscountValue { get; private set; }

    /// <summary>Optional cap on the computed discount (useful for percentage discounts).</summary>
    public decimal? MaxDiscountAmount { get; private set; }

    /// <summary>Minimum cart subtotal required for this promotion to apply.</summary>
    public decimal? MinimumOrderAmount { get; private set; }

    /// <summary>Total usage allowed across all customers. Null = unlimited.</summary>
    public int? UsageLimit { get; private set; }

    /// <summary>Maximum times a single customer may use this promotion. Null = unlimited.</summary>
    public int? PerCustomerUsageLimit { get; private set; }

    /// <summary>UTC datetime when the promotion becomes eligible. Null = no start restriction.</summary>
    public DateTime? StartsAt { get; private set; }

    /// <summary>UTC datetime when the promotion expires. Null = no expiry.</summary>
    public DateTime? ExpiresAt { get; private set; }

    public PromotionApplicabilityType Applicability { get; private set; }

    /// <summary>When true, only customers placing their first order can use this promotion.</summary>
    public bool IsFirstOrderOnly { get; private set; }

    /// <summary>Admin-controlled active flag. Inactive promotions are never applied.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Total times this promotion has been successfully used.</summary>
    public int UsageCount { get; private set; }

    /// <summary>
    /// PostgreSQL xmin system column — used as an optimistic concurrency token.
    /// EF Core reads this on load and checks it on UPDATE; a concurrent
    /// increment will change xmin and cause a DbUpdateConcurrencyException.
    /// </summary>
    public uint Version { get; private set; }

    // Navigation — loaded only when needed
    private readonly List<PromotionProduct> _products = [];
    private readonly List<PromotionCategory> _categories = [];
    public IReadOnlyList<PromotionProduct> Products => _products.AsReadOnly();
    public IReadOnlyList<PromotionCategory> Categories => _categories.AsReadOnly();

    // -----------------------------------------------------------------------
    // Behaviour — admin management
    // -----------------------------------------------------------------------

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;

    public void Update(
        string name,
        string? description,
        DiscountType discountType,
        decimal discountValue,
        decimal? maxDiscountAmount,
        decimal? minimumOrderAmount,
        int? usageLimit,
        int? perCustomerUsageLimit,
        DateTime? startsAt,
        DateTime? expiresAt,
        PromotionApplicabilityType applicability,
        bool isFirstOrderOnly)
    {
        ValidateName(name);
        ValidateDiscountValue(discountType, discountValue);
        ValidateDates(startsAt, expiresAt);

        if (maxDiscountAmount is < 0m)
            throw new ArgumentException("Max discount amount must be >= 0.", nameof(maxDiscountAmount));
        if (minimumOrderAmount is < 0m)
            throw new ArgumentException("Minimum order amount must be >= 0.", nameof(minimumOrderAmount));
        if (usageLimit is < 1)
            throw new ArgumentException("Usage limit must be >= 1 when specified.", nameof(usageLimit));
        if (perCustomerUsageLimit is < 1)
            throw new ArgumentException("Per-customer usage limit must be >= 1 when specified.", nameof(perCustomerUsageLimit));

        Name = name.Trim();
        Description = description?.Trim();
        DiscountType = discountType;
        DiscountValue = discountValue;
        MaxDiscountAmount = maxDiscountAmount;
        MinimumOrderAmount = minimumOrderAmount;
        UsageLimit = usageLimit;
        PerCustomerUsageLimit = perCustomerUsageLimit;
        StartsAt = startsAt;
        ExpiresAt = expiresAt;
        Applicability = applicability;
        IsFirstOrderOnly = isFirstOrderOnly;
    }

    // -----------------------------------------------------------------------
    // Behaviour — eligibility checks (pure domain logic, no I/O)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Validates whether this promotion is currently active and within its time window.
    /// Does not check usage counts — that requires database reads.
    /// </summary>
    public bool IsCurrentlyValid(DateTime utcNow)
    {
        if (!IsActive) return false;
        if (StartsAt.HasValue && utcNow < StartsAt.Value) return false;
        if (ExpiresAt.HasValue && utcNow >= ExpiresAt.Value) return false;
        return true;
    }

    /// <summary>Returns false when the global usage limit has been reached.</summary>
    public bool HasRemainingUsage() =>
        UsageLimit is null || UsageCount < UsageLimit;

    /// <summary>
    /// Calculates the discount amount for the given eligible subtotal.
    /// The result is capped so it never exceeds the eligible subtotal (no negative totals).
    /// </summary>
    public decimal CalculateDiscount(decimal eligibleSubtotal)
    {
        if (eligibleSubtotal <= 0m) return 0m;

        decimal discount = DiscountType switch
        {
            DiscountType.Percentage =>
                Math.Round(eligibleSubtotal * DiscountValue / 100m, 2, MidpointRounding.ToEven),
            DiscountType.FixedAmount =>
                DiscountValue,
            _ => 0m
        };

        // Apply per-discount maximum cap
        if (MaxDiscountAmount.HasValue)
            discount = Math.Min(discount, MaxDiscountAmount.Value);

        // Never let discount exceed the eligible amount
        discount = Math.Min(discount, eligibleSubtotal);

        return discount;
    }

    /// <summary>Atomically increments the usage counter. Called inside checkout transaction.</summary>
    public void IncrementUsage() => UsageCount++;

    // -----------------------------------------------------------------------
    // Product / category target management (admin)
    // -----------------------------------------------------------------------

    public void SetTargetProducts(IEnumerable<Guid> productIds)
    {
        _products.Clear();
        foreach (var id in productIds.Distinct())
            _products.Add(new PromotionProduct { PromotionId = Id, ProductId = id });
    }

    public void SetTargetCategories(IEnumerable<Guid> categoryIds)
    {
        _categories.Clear();
        foreach (var id in categoryIds.Distinct())
            _categories.Add(new PromotionCategory { PromotionId = Id, CategoryId = id });
    }

    // -----------------------------------------------------------------------
    // Guards
    // -----------------------------------------------------------------------

    public static string NormaliseCouponCode(string code) =>
        code.Trim().ToUpperInvariant();

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Promotion name is required.", nameof(name));
    }

    private static void ValidateCouponCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Coupon code is required.", nameof(code));
        if (code.Trim().Length < 3)
            throw new ArgumentException("Coupon code must be at least 3 characters.", nameof(code));
        if (code.Trim().Length > 50)
            throw new ArgumentException("Coupon code must not exceed 50 characters.", nameof(code));
    }

    private static void ValidateDiscountValue(DiscountType discountType, decimal discountValue)
    {
        switch (discountType)
        {
            case DiscountType.Percentage when discountValue <= 0m:
                throw new ArgumentException("Percentage discount must be > 0.", nameof(discountValue));
            case DiscountType.Percentage when discountValue > 100m:
                throw new ArgumentException("Percentage discount must be <= 100.", nameof(discountValue));
            case DiscountType.FixedAmount when discountValue <= 0m:
                throw new ArgumentException("Fixed discount must be > 0.", nameof(discountValue));
        }
    }

    private static void ValidateDates(DateTime? startsAt, DateTime? expiresAt)
    {
        if (startsAt.HasValue && expiresAt.HasValue && expiresAt.Value <= startsAt.Value)
            throw new ArgumentException("Expiry must be after start date.", nameof(expiresAt));
    }
}
