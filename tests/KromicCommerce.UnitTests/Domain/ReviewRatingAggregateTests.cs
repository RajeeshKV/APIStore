using KromicCommerce.Domain.Catalog;

namespace KromicCommerce.UnitTests.Domain;

public sealed class ReviewRatingAggregateTests
{
    [Fact]
    public void Empty_set_yields_zero_not_null()
    {
        var aggregate = ReviewRatingAggregate.FromRatings([]);

        aggregate.Average.Should().Be(0m);
        aggregate.Count.Should().Be(0);
        aggregate.Should().Be(ReviewRatingAggregate.Empty);
    }

    [Fact]
    public void Single_rating_is_the_average()
    {
        var aggregate = ReviewRatingAggregate.FromRatings([5]);

        aggregate.Average.Should().Be(5m);
        aggregate.Count.Should().Be(1);
    }

    [Fact]
    public void Computes_a_two_decimal_average()
    {
        // 4 + 4 + 5 = 13 / 3 = 4.333... -> 4.33
        var aggregate = ReviewRatingAggregate.FromRatings([4, 4, 5]);

        aggregate.Average.Should().Be(4.33m);
        aggregate.Count.Should().Be(3);
    }

    [Fact]
    public void Rounds_half_away_from_zero()
    {
        // 4 + 5 = 9 / 2 = 4.5 exactly; 4 + 4 + 4 + 5 = 17/4 = 4.25 -> 4.25
        ReviewRatingAggregate.FromRatings([4, 5]).Average.Should().Be(4.5m);
        ReviewRatingAggregate.FromRatings([4, 4, 4, 5]).Average.Should().Be(4.25m);

        // 3 + 4 = 7 / 2 = 3.5 -> AwayFromZero gives 3.5 (no digit to round)
        ReviewRatingAggregate.FromRatings([3, 4]).Average.Should().Be(3.5m);
    }

    [Fact]
    public void Average_is_clamped_to_two_decimal_places()
    {
        // 1+2+3+4+5+5 = 20 / 6 = 3.3333...
        var aggregate = ReviewRatingAggregate.FromRatings([1, 2, 3, 4, 5, 5]);

        decimal.Round(aggregate.Average, 2).Should().Be(aggregate.Average);
    }

    [Fact]
    public void Removing_the_last_review_returns_to_zero_not_the_previous_value()
    {
        // This is the drift case that an incrementing counter gets wrong: after removing the
        // only published review the aggregate must be Empty, never the stale average.
        ReviewRatingAggregate.FromRatings([]).Should().Be(ReviewRatingAggregate.Empty);
        ReviewRatingAggregate.Empty.Average.Should().Be(0m);
    }

    [Fact]
    public void Recalculation_is_order_independent()
    {
        var forward = ReviewRatingAggregate.FromRatings([1, 2, 3, 4, 5]);
        var reverse = ReviewRatingAggregate.FromRatings([5, 4, 3, 2, 1]);

        forward.Should().Be(reverse);
    }

    [Fact]
    public void SetRatingAggregate_writes_both_values_onto_the_product()
    {
        var product = Product.Create("Widget", "widget", null, 10m, null, null);

        product.RatingAverage.Should().Be(0m);
        product.RatingCount.Should().Be(0);

        product.SetRatingAggregate(ReviewRatingAggregate.FromRatings([5, 4]));

        product.RatingAverage.Should().Be(4.5m);
        product.RatingCount.Should().Be(2);
    }

    [Fact]
    public void SetRatingAggregate_can_reset_a_product_back_to_zero()
    {
        var product = Product.Create("Widget", "widget", null, 10m, null, null);
        product.SetRatingAggregate(ReviewRatingAggregate.FromRatings([5, 5, 5]));

        product.SetRatingAggregate(ReviewRatingAggregate.Empty);

        product.RatingAverage.Should().Be(0m);
        product.RatingCount.Should().Be(0);
    }
}