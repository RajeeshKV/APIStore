namespace KromicCommerce.Domain.Store;

/// <summary>
/// Delivery configuration owned by BusinessSettings.
/// Stored as flattened columns in the business_settings table (EF owned entity).
/// All monetary values in the store's configured currency.
/// </summary>
public sealed class DeliverySettings : ValueObject
{
    public static DeliverySettings Default() => new()
    {
        FlatFeeAmount = 0m,
        FreeShippingThreshold = null,
        CodEnabled = true,
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

    /// <summary>Extra fee charged for COD orders. 0 = no surcharge.</summary>
    public decimal CodExtraFee { get; private set; }

    /// <summary>Days taken to process/pack before dispatch.</summary>
    public int ProcessingDays { get; private set; }

    /// <summary>Minimum estimated delivery days (after dispatch).</summary>
    public int MinDeliveryDays { get; private set; }

    /// <summary>Maximum estimated delivery days (after dispatch).</summary>
    public int MaxDeliveryDays { get; private set; }

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
            CodExtraFee = codExtraFee,
            ProcessingDays = processingDays,
            MinDeliveryDays = minDeliveryDays,
            MaxDeliveryDays = maxDeliveryDays
        };
    }

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
