namespace KromicCommerce.Domain.Store;

/// <summary>
/// Delivery / shipping configuration owned by BusinessSettings.
///
/// This is the SINGLE source of truth for cash-on-delivery. COD is a shipping concern
/// (a surcharge on the delivery of an order), so both the availability flag and the fee
/// live here and nowhere else. Integrations screens must not introduce a second COD store.
///
/// Invariants enforced by <see cref="Create"/>:
///   - No negative monetary values or day counts.
///   - MaxDeliveryDays &gt;= MinDeliveryDays.
///   - <see cref="EffectiveCodFee"/> is zero whenever <see cref="CodEnabled"/> is false, so a
///     configured fee can never be charged for a disabled payment method. The configured
///     <see cref="CodExtraFee"/> itself is PRESERVED while COD is off, so that re-enabling COD
///     restores the surcharge the merchant set instead of silently charging nothing.
/// </summary>
public sealed class DeliverySettings : ValueObject
{
    public static DeliverySettings Default() => new()
    {
        FlatFeeAmount = 0m,
        FreeShippingThreshold = null,
        CodEnabled = false,
        CodExtraFee = 0m,
        ProcessingDays = 1,
        MinDeliveryDays = 3,
        MaxDeliveryDays = 7
    };

    /// <summary>Flat shipping fee charged per order. 0 = free shipping for all orders.</summary>
    public decimal FlatFeeAmount { get; private set; }

    /// <summary>
    /// Order subtotal above which shipping is free.
    /// Null means flat fee always applies (no free-shipping threshold).
    /// </summary>
    public decimal? FreeShippingThreshold { get; private set; }

    /// <summary>Cash-on-delivery enabled for this store.</summary>
    public bool CodEnabled { get; private set; }

    /// <summary>
    /// The COD surcharge the merchant configured. Retained even while
    /// <see cref="CodEnabled"/> is false, so toggling COD off and on again does not lose it.
    /// Use <see cref="EffectiveCodFee"/> for anything that feeds a price — never this value.
    /// </summary>
    public decimal CodExtraFee { get; private set; }

    /// <summary>Days taken to process/pack before dispatch.</summary>
    public int ProcessingDays { get; private set; }

    /// <summary>Minimum estimated delivery days (after dispatch).</summary>
    public int MinDeliveryDays { get; private set; }

    /// <summary>Maximum estimated delivery days (after dispatch).</summary>
    public int MaxDeliveryDays { get; private set; }

    // -----------------------------------------------------------------------
    // Derived values — the only values pricing and availability logic may read
    // -----------------------------------------------------------------------

    /// <summary>
    /// The COD surcharge that may actually be charged. Zero whenever COD is unavailable,
    /// so disabling COD is sufficient to remove the fee from every calculation without
    /// a second code path. This is the only value pricing code may read.
    /// </summary>
    public decimal EffectiveCodFee => CodEnabled ? CodExtraFee : 0m;

    /// <summary>
    /// Whether a new COD order may be accepted. This is the single availability predicate
    /// used by checkout, cart pricing previews and the storefront settings projection.
    /// </summary>
    public bool IsCodAvailable => CodEnabled;

    // -----------------------------------------------------------------------
    // Factory / update
    // -----------------------------------------------------------------------

    public static DeliverySettings Create(
        decimal flatFeeAmount,
        decimal? freeShippingThreshold,
        bool codEnabled,
        decimal codExtraFee,
        int processingDays,
        int minDeliveryDays,
        int maxDeliveryDays)
    {
        if (flatFeeAmount < 0)
            throw new ArgumentException("Flat fee amount must be >= 0.", nameof(flatFeeAmount));
        if (freeShippingThreshold is < 0)
            throw new ArgumentException("Free-shipping threshold must be >= 0.", nameof(freeShippingThreshold));
        if (codExtraFee < 0)
            throw new ArgumentException("COD extra fee must be >= 0.", nameof(codExtraFee));
        if (processingDays < 0)
            throw new ArgumentException("Processing days must be >= 0.", nameof(processingDays));
        if (minDeliveryDays < 0)
            throw new ArgumentException("Min delivery days must be >= 0.", nameof(minDeliveryDays));
        if (maxDeliveryDays < minDeliveryDays)
            throw new ArgumentException("Max delivery days must be >= min delivery days.", nameof(maxDeliveryDays));

        return new DeliverySettings
        {
            FlatFeeAmount = flatFeeAmount,
            FreeShippingThreshold = freeShippingThreshold,
            CodEnabled = codEnabled,
            // The configured fee is stored as submitted even when COD is off, so toggling COD
            // back on restores the merchant's surcharge. EffectiveCodFee is what guarantees a
            // disabled method can never be charged — the admin UI re-sending a stale fee while
            // COD is off is therefore harmless rather than something we need to normalise away.
            CodExtraFee = codExtraFee,
            ProcessingDays = processingDays,
            MinDeliveryDays = minDeliveryDays,
            MaxDeliveryDays = maxDeliveryDays
        };
    }

    /// <summary>
    /// Returns a copy with only the COD availability flag changed and every other
    /// shipping value preserved — including the configured <see cref="CodExtraFee"/>, so
    /// toggling COD off and on again is lossless.
    ///
    /// This is an in-model convenience, not a second configuration surface: COD is reachable
    /// from exactly one endpoint (UpdateDeliverySettings), which calls this to preserve the
    /// fee and delivery-day estimates when only availability needs to change.
    /// </summary>
    public DeliverySettings WithCodEnabled(bool enabled) => Create(
        FlatFeeAmount,
        FreeShippingThreshold,
        enabled,
        CodExtraFee,
        ProcessingDays,
        MinDeliveryDays,
        MaxDeliveryDays);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FlatFeeAmount;
        yield return FreeShippingThreshold;
        yield return CodEnabled;
        yield return CodExtraFee;
        yield return ProcessingDays;
        yield return MinDeliveryDays;
        yield return MaxDeliveryDays;
    }
}
