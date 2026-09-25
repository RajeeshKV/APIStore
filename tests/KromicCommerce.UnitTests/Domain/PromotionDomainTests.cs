using KromicCommerce.Domain.Promotions;

namespace KromicCommerce.UnitTests.Domain;

public sealed class PromotionDomainTests
{
    // -----------------------------------------------------------------------
    // Factory helpers
    // -----------------------------------------------------------------------

    private static Promotion BuildPromotion(
        string name = "Summer Sale",
        string couponCode = "SUMMER10",
        DiscountType discountType = DiscountType.Percentage,
        decimal discountValue = 10m,
        decimal? maxDiscountAmount = null,
        decimal? minimumOrderAmount = null,
        int? usageLimit = null,
        int? perCustomerUsageLimit = null,
        DateTime? startsAt = null,
        DateTime? expiresAt = null,
        PromotionApplicabilityType applicability = PromotionApplicabilityType.EntireOrder,
        bool isFirstOrderOnly = false)
        => Promotion.Create(name, null, couponCode, discountType, discountValue,
            maxDiscountAmount, minimumOrderAmount, usageLimit, perCustomerUsageLimit,
            startsAt, expiresAt, applicability, isFirstOrderOnly);

    // -----------------------------------------------------------------------
    // Create — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_sets_all_fields_correctly()
    {
        var p = BuildPromotion(name: "Promo", couponCode: "CODE123",
            discountType: DiscountType.Percentage, discountValue: 15m);

        p.Name.Should().Be("Promo");
        p.CouponCode.Should().Be("CODE123");
        p.DiscountType.Should().Be(DiscountType.Percentage);
        p.DiscountValue.Should().Be(15m);
        p.IsActive.Should().BeFalse();
        p.UsageCount.Should().Be(0);
    }

    [Fact]
    public void Create_normalises_coupon_code_to_uppercase()
    {
        var p = BuildPromotion(couponCode: "welcome10");
        p.CouponCode.Should().Be("WELCOME10");
    }

    [Fact]
    public void Create_trims_and_normalises_mixed_case_code()
    {
        var p = BuildPromotion(couponCode: "  WeLcOmE10  ");
        p.CouponCode.Should().Be("WELCOME10");
    }

    // -----------------------------------------------------------------------
    // Create — validation failures
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_for_empty_name(string name)
    {
        var act = () => BuildPromotion(name: name);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("AB")] // < 3 chars
    public void Create_throws_for_invalid_coupon_code(string code)
    {
        var act = () => BuildPromotion(couponCode: code);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_for_percentage_discount_above_100()
    {
        var act = () => BuildPromotion(discountType: DiscountType.Percentage, discountValue: 101m);
        act.Should().Throw<ArgumentException>().WithMessage("*<= 100*");
    }

    [Fact]
    public void Create_throws_for_percentage_discount_of_zero()
    {
        var act = () => BuildPromotion(discountType: DiscountType.Percentage, discountValue: 0m);
        act.Should().Throw<ArgumentException>().WithMessage("*> 0*");
    }

    [Fact]
    public void Create_throws_for_negative_fixed_discount()
    {
        var act = () => BuildPromotion(discountType: DiscountType.FixedAmount, discountValue: -1m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_for_zero_fixed_discount()
    {
        var act = () => BuildPromotion(discountType: DiscountType.FixedAmount, discountValue: 0m);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_when_expiry_before_start()
    {
        var start = DateTime.UtcNow.AddDays(5);
        var expiry = DateTime.UtcNow.AddDays(1);
        var act = () => BuildPromotion(startsAt: start, expiresAt: expiry);
        act.Should().Throw<ArgumentException>().WithMessage("*Expiry*");
    }

    [Fact]
    public void Create_throws_for_usage_limit_of_zero()
    {
        var act = () => BuildPromotion(usageLimit: 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_throws_for_negative_minimum_order_amount()
    {
        var act = () => BuildPromotion(minimumOrderAmount: -1m);
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Activate / Deactivate
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_sets_IsActive_true()
    {
        var p = BuildPromotion();
        p.IsActive.Should().BeFalse();
        p.Activate();
        p.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_sets_IsActive_false()
    {
        var p = BuildPromotion();
        p.Activate();
        p.Deactivate();
        p.IsActive.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // IsCurrentlyValid — time window checks
    // -----------------------------------------------------------------------

    [Fact]
    public void IsCurrentlyValid_returns_false_when_inactive()
    {
        var p = BuildPromotion();
        // Active = false by default
        p.IsCurrentlyValid(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsCurrentlyValid_returns_true_when_active_and_no_time_window()
    {
        var p = BuildPromotion();
        p.Activate();
        p.IsCurrentlyValid(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsCurrentlyValid_returns_false_before_start_date()
    {
        var p = BuildPromotion(startsAt: DateTime.UtcNow.AddDays(1));
        p.Activate();
        p.IsCurrentlyValid(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsCurrentlyValid_returns_false_after_expiry()
    {
        var p = BuildPromotion(
            startsAt: DateTime.UtcNow.AddDays(-10),
            expiresAt: DateTime.UtcNow.AddDays(-1));
        p.Activate();
        p.IsCurrentlyValid(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsCurrentlyValid_returns_true_within_window()
    {
        var p = BuildPromotion(
            startsAt: DateTime.UtcNow.AddDays(-1),
            expiresAt: DateTime.UtcNow.AddDays(1));
        p.Activate();
        p.IsCurrentlyValid(DateTime.UtcNow).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // HasRemainingUsage
    // -----------------------------------------------------------------------

    [Fact]
    public void HasRemainingUsage_true_when_no_limit()
    {
        var p = BuildPromotion(usageLimit: null);
        p.HasRemainingUsage().Should().BeTrue();
    }

    [Fact]
    public void HasRemainingUsage_false_when_limit_reached()
    {
        var p = BuildPromotion(usageLimit: 1);
        p.IncrementUsage(); // UsageCount = 1 = limit
        p.HasRemainingUsage().Should().BeFalse();
    }

    [Fact]
    public void HasRemainingUsage_true_when_below_limit()
    {
        var p = BuildPromotion(usageLimit: 5);
        p.IncrementUsage();
        p.IncrementUsage();
        p.HasRemainingUsage().Should().BeTrue();
    }

    [Fact]
    public void IncrementUsage_increments_counter()
    {
        var p = BuildPromotion();
        p.IncrementUsage();
        p.IncrementUsage();
        p.UsageCount.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // CalculateDiscount — Percentage
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateDiscount_percentage_computes_correctly()
    {
        var p = BuildPromotion(discountType: DiscountType.Percentage, discountValue: 10m);
        p.CalculateDiscount(1000m).Should().Be(100m);
    }

    [Fact]
    public void CalculateDiscount_percentage_applies_max_cap()
    {
        var p = BuildPromotion(discountType: DiscountType.Percentage,
            discountValue: 50m, maxDiscountAmount: 100m);
        // 50% of 500 = 250, but capped at 100
        p.CalculateDiscount(500m).Should().Be(100m);
    }

    [Fact]
    public void CalculateDiscount_percentage_does_not_exceed_subtotal()
    {
        var p = BuildPromotion(discountType: DiscountType.Percentage, discountValue: 100m);
        // 100% of 200 = 200, capped at 200
        p.CalculateDiscount(200m).Should().Be(200m);
    }

    [Fact]
    public void CalculateDiscount_returns_zero_for_zero_subtotal()
    {
        var p = BuildPromotion(discountType: DiscountType.Percentage, discountValue: 20m);
        p.CalculateDiscount(0m).Should().Be(0m);
    }

    // -----------------------------------------------------------------------
    // CalculateDiscount — FixedAmount
    // -----------------------------------------------------------------------

    [Fact]
    public void CalculateDiscount_fixed_returns_exact_amount()
    {
        var p = BuildPromotion(discountType: DiscountType.FixedAmount, discountValue: 50m);
        p.CalculateDiscount(200m).Should().Be(50m);
    }

    [Fact]
    public void CalculateDiscount_fixed_is_capped_at_subtotal()
    {
        // Fixed ₹500 on a ₹200 cart → capped at ₹200 (no negative total)
        var p = BuildPromotion(discountType: DiscountType.FixedAmount, discountValue: 500m);
        p.CalculateDiscount(200m).Should().Be(200m);
    }

    [Fact]
    public void CalculateDiscount_fixed_with_max_cap()
    {
        var p = BuildPromotion(discountType: DiscountType.FixedAmount,
            discountValue: 200m, maxDiscountAmount: 100m);
        p.CalculateDiscount(500m).Should().Be(100m);
    }

    // -----------------------------------------------------------------------
    // NormaliseCouponCode
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("welcome10", "WELCOME10")]
    [InlineData("SUMMER", "SUMMER")]
    [InlineData("  mixed  ", "MIXED")]
    public void NormaliseCouponCode_uppercases_and_trims(string input, string expected)
    {
        Promotion.NormaliseCouponCode(input).Should().Be(expected);
    }
}
