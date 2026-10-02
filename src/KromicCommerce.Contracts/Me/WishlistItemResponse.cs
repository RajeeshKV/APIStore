using KromicCommerce.Contracts.Catalog;

namespace KromicCommerce.Contracts.Me;

/// <summary>
/// One saved product on the customer's wishlist, with the product snapshot needed to render a
/// card without a second round trip.
///
/// Stock is exposed only as <see cref="StockAvailability"/> and <see cref="CanPurchase"/>.
/// Raw OnHand/Reserved are deliberately absent: they are an internal inventory concern, and
/// publishing them invites clients to gate the UI on a number that reservation can invalidate
/// before checkout completes.
/// </summary>
public sealed record WishlistItemResponse(
    Guid Id,
    Guid ProductId,
    Guid? ProductVariantId,
    string ProductName,
    string ProductSlug,
    string? ProductImageUrl,
    decimal BasePrice,
    decimal? VariantPrice,
    decimal EffectivePrice,
    string CurrencyCode,
    StockAvailability StockAvailability,
    bool CanPurchase,
    bool IsProductActive,
    DateTime AddedAtUtc);