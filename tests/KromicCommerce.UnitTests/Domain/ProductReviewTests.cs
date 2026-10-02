using KromicCommerce.Domain.Catalog;
using KromicCommerce.Domain.Catalog.Events;

namespace KromicCommerce.UnitTests.Domain;

public sealed class ProductReviewTests
{
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();

    private static ProductReview Valid(ReviewStatus status = ReviewStatus.Pending) =>
        ProductReview.Create(Customer, Product, null, 4, "Great", "Solid product.", false, status);

    private static ProductReview WithRating(int rating) =>
        ProductReview.Create(Customer, Product, null, rating, "Great", "Solid product.", false, ReviewStatus.Pending);

    private static MediaAsset Asset(string publicId = "reviews/abc123") =>
        MediaAsset.Create(publicId, $"https://res.cloudinary.com/demo/image/upload/{publicId}.jpg", "jpg", 800, 600, null);

    // -----------------------------------------------------------------------
    // Rating bounds
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Create_accepts_ratings_in_range(int rating)
        => WithRating(rating).Rating.Should().Be(rating);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(int.MaxValue)]
    public void Create_rejects_rating_outside_one_to_five(int rating) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductReview.Create(Customer, Product, null, rating, null, "body", false, ReviewStatus.Pending));

    [Theory]
    [InlineData(0)]
    [InlineData(int.MinValue)]
    public void Edit_rejects_rating_outside_one_to_five(int rating) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid().Edit(rating, null, "body"));

    // -----------------------------------------------------------------------
    // Content validation
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_missing_body(string? body) =>
        Assert.Throws<ArgumentException>(() =>
            ProductReview.Create(Customer, Product, null, 4, null, body!, false, ReviewStatus.Pending));

    [Fact]
    public void Create_rejects_body_over_max_length()
    {
        var tooLong = new string('x', ProductReview.BodyMaxLength + 1);

        Assert.Throws<ArgumentException>(() =>
            ProductReview.Create(Customer, Product, null, 4, null, tooLong, false, ReviewStatus.Pending));
    }

    [Fact]
    public void Create_rejects_title_over_max_length()
    {
        var tooLong = new string('t', ProductReview.TitleMaxLength + 1);

        Assert.Throws<ArgumentException>(() =>
            ProductReview.Create(Customer, Product, null, 4, tooLong, "body", false, ReviewStatus.Pending));
    }

    [Fact]
    public void Create_trims_title_and_body()
    {
        var review = ProductReview.Create(Customer, Product, null, 4, "  Nice  ", "  Good stuff.  ", false, ReviewStatus.Pending);

        review.Title.Should().Be("Nice");
        review.Body.Should().Be("Good stuff.");
    }

    [Fact]
    public void Create_normalises_empty_title_to_null()
        => ProductReview.Create(Customer, Product, null, 4, "   ", "body", false, ReviewStatus.Pending)
            .Title.Should().BeNull();

    // -----------------------------------------------------------------------
    // Identity guards
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_rejects_empty_customer_id() =>
        Assert.Throws<ArgumentException>(() =>
            ProductReview.Create(Guid.Empty, Product, null, 4, null, "body", false, ReviewStatus.Pending));

    [Fact]
    public void Create_rejects_empty_product_id() =>
        Assert.Throws<ArgumentException>(() =>
            ProductReview.Create(Customer, Guid.Empty, null, 4, null, "body", false, ReviewStatus.Pending));

    [Fact]
    public void Create_treats_empty_variant_id_as_null() =>
        ProductReview.Create(Customer, Product, Guid.Empty, 4, null, "body", false, ReviewStatus.Pending)
            .ProductVariantId.Should().BeNull();

    // -----------------------------------------------------------------------
    // Edit invariance — the guard that is easiest to break by accident
    // -----------------------------------------------------------------------

    [Fact]
    public void Edit_changes_content_only()
    {
        var review = ProductReview.Create(
            Customer, Product, null, 2, "Original", "Original body.", true, ReviewStatus.Published);
        var originalPublishedAt = review.PublishedAtUtc;

        review.Edit(5, "Rewritten", "Rewritten body.");

        review.Rating.Should().Be(5);
        review.Title.Should().Be("Rewritten");
        review.Body.Should().Be("Rewritten body.");

        // Everything below must survive an edit untouched. If any of these move, a customer
        // editing a published review silently re-enters moderation or loses their verified badge.
        review.Status.Should().Be(ReviewStatus.Published);
        review.PublishedAtUtc.Should().Be(originalPublishedAt);
        review.IsVerifiedPurchase.Should().BeTrue();
        review.CustomerId.Should().Be(Customer);
        review.ProductId.Should().Be(Product);
        review.ModerationReason.Should().BeNull();
    }

    [Fact]
    public void Edit_does_not_change_images_or_helpful_count()
    {
        var review = Valid();
        review.AddImage(Asset(), 0);
        review.SetHelpfulCount(7);

        review.Edit(1, null, "revised");

        review.Images.Should().HaveCount(1);
        review.HelpfulCount.Should().Be(7);
    }

    // -----------------------------------------------------------------------
    // Status transitions
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_with_pending_stamps_no_publish_time() =>
        Valid(ReviewStatus.Pending).PublishedAtUtc.Should().BeNull();

    [Fact]
    public void Create_with_published_stamps_publish_time() =>
        Valid(ReviewStatus.Published).PublishedAtUtc.Should().NotBeNull();

    [Fact]
    public void SetStatus_publish_stamps_publish_time()
    {
        var review = Valid(ReviewStatus.Pending);
        review.SetStatus(ReviewStatus.Published);

        review.Status.Should().Be(ReviewStatus.Published);
        review.PublishedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void SetStatus_restamps_publish_time_on_republication()
    {
        // Leaving Published clears PublishedAtUtc, so re-approval legitimately becomes a fresh
        // publication. Sorting by recency should reflect when the review became visible again.
        var review = Valid(ReviewStatus.Published);
        var first = review.PublishedAtUtc;

        review.SetStatus(ReviewStatus.Rejected, "nope");
        review.PublishedAtUtc.Should().BeNull();

        review.SetStatus(ReviewStatus.Published);

        review.PublishedAtUtc.Should().NotBeNull();
        review.PublishedAtUtc.Should().NotBe(first);
    }

    [Fact]
    public void SetStatus_unpublish_clears_publish_time()
    {
        var review = Valid(ReviewStatus.Published);
        review.SetStatus(ReviewStatus.Rejected, "spam");

        review.PublishedAtUtc.Should().BeNull();
        review.ModerationReason.Should().Be("spam");
    }

    [Fact]
    public void SetStatus_to_same_value_is_a_no_op()
    {
        var review = Valid(ReviewStatus.Published);
        var before = review.PublishedAtUtc;

        review.SetStatus(ReviewStatus.Published);
        review.SetStatus(ReviewStatus.Published);

        review.Status.Should().Be(ReviewStatus.Published);
        review.PublishedAtUtc.Should().Be(before);
    }

    [Fact]
    public void SetStatus_clears_moderation_reason_on_republish()
    {
        var review = Valid(ReviewStatus.Published);
        review.SetStatus(ReviewStatus.Rejected, "wrong product");
        review.SetStatus(ReviewStatus.Published);

        review.ModerationReason.Should().BeNull();
    }

    [Fact]
    public void SetStatus_truncates_over_long_moderation_reason()
    {
        var review = Valid();
        var reason = new string('r', ProductReview.ModerationReasonMaxLength + 40);

        review.SetStatus(ReviewStatus.Rejected, reason);

        review.ModerationReason!.Length.Should().Be(ProductReview.ModerationReasonMaxLength);
    }

    // -----------------------------------------------------------------------
    // Images
    // -----------------------------------------------------------------------

    [Fact]
    public void AddImage_accepts_up_to_the_maximum()
    {
        var review = Valid();
        for (var i = 0; i < ProductReview.MaxImages; i++)
            review.AddImage(Asset($"reviews/img{i}"), i);

        review.Images.Should().HaveCount(ProductReview.MaxImages);
    }

    [Fact]
    public void AddImage_rejects_the_fourth_image()
    {
        var review = Valid();
        for (var i = 0; i < ProductReview.MaxImages; i++)
            review.AddImage(Asset($"reviews/img{i}"), i);

        Assert.Throws<ArgumentException>(() => review.AddImage(Asset("reviews/overflow"), 3));
    }

    [Fact]
    public void RemoveImage_rejects_an_image_from_another_review()
    {
        var mine = Valid();
        var theirs = Valid();
        mine.AddImage(Asset("reviews/mine"), 0);
        theirs.AddImage(Asset("reviews/theirs"), 0);

        Assert.Throws<ArgumentException>(() => mine.RemoveImage(theirs.Images.First().Id));
        mine.Images.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveImage_removes_own_image()
    {
        var review = Valid();
        review.AddImage(Asset(), 0);

        review.RemoveImage(review.Images.First().Id);

        review.Images.Should().BeEmpty();
    }

    [Fact]
    public void RemoveImage_frees_a_slot_so_a_new_image_can_be_added()
    {
        var review = Valid();
        for (var i = 0; i < ProductReview.MaxImages; i++)
            review.AddImage(Asset($"reviews/img{i}"), i);

        review.RemoveImage(review.Images.First().Id);
        review.AddImage(Asset("reviews/replacement"), 0);

        review.Images.Should().HaveCount(ProductReview.MaxImages);
    }

    // -----------------------------------------------------------------------
    // Helpful count / ownership
    // -----------------------------------------------------------------------

    [Fact]
    public void SetHelpfulCount_rejects_negative() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid().SetHelpfulCount(-1));

    [Fact]
    public void SetHelpfulCount_accepts_zero() => Valid().SetHelpfulCount(0);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsOwnedBy_compares_against_the_creating_customer(bool expected)
        => Valid().IsOwnedBy(expected ? Customer : Guid.NewGuid()).Should().Be(expected);

    [Fact]
    public void SetVerifiedPurchase_is_the_only_way_to_change_the_badge()
    {
        var review = Valid();
        review.IsVerifiedPurchase.Should().BeFalse();

        review.SetVerifiedPurchase(true);

        review.IsVerifiedPurchase.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Domain events
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_raises_submitted_event()
        => Valid().DomainEvents.Should().ContainSingle(e => e is ProductReviewSubmittedEvent);

    [Fact]
    public void SetStatus_raises_status_changed_event_with_both_states()
    {
        var review = Valid(ReviewStatus.Pending);
        review.SetStatus(ReviewStatus.Published);

        var raised = review.DomainEvents.OfType<ProductReviewStatusChangedEvent>().Single();
        raised.PreviousStatus.Should().Be(ReviewStatus.Pending);
        raised.NewStatus.Should().Be(ReviewStatus.Published);
        raised.ProductId.Should().Be(Product);
    }

    [Fact]
    public void Edit_raises_edited_event()
    {
        var review = Valid();
        review.Edit(3, null, "changed");

        review.DomainEvents.OfType<ProductReviewEditedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void RaiseDeleted_carries_the_product_id_for_aggregate_recalculation()
    {
        var review = Valid();
        review.RaiseDeleted();

        var raised = review.DomainEvents.OfType<ProductReviewDeletedEvent>().Single();
        raised.ProductId.Should().Be(Product);
        raised.CustomerId.Should().Be(Customer);
    }
}