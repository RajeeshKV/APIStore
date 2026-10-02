using System.Reflection;
using KromicCommerce.Application.Features.Catalog.Reviews;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Domain.Catalog;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Trust-boundary tests for reviews.
///
/// These assert properties of the request surface and of the pure image-validation helper,
/// because those are the parts an attacker actually controls. Handlers that need a database are
/// covered by <c>ReviewIntegrationTests</c> against real PostgreSQL.
/// </summary>
public sealed class ReviewTrustBoundaryTests
{
    // -----------------------------------------------------------------------
    // The request DTOs must not be able to express ownership or verification
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateReviewRequest_cannot_supply_a_customer_id()
    {
        // The handler reads CustomerId from ICurrentUserService. If this DTO ever grows the
        // field, a client could submit a review as somebody else.
        typeof(CreateReviewRequest)
            .GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(new[]
            {
                "CustomerId", "Customer", "UserId", "CustomerEmail"
            });
    }

    [Fact]
    public void CreateReviewRequest_cannot_claim_verified_purchase()
    {
        // Verified purchase is derived from delivered orders server-side. Accepting it from a
        // body would make the badge meaningless and the anti-spam guard worthless.
        typeof(CreateReviewRequest)
            .GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(new[]
            {
                "IsVerifiedPurchase", "VerifiedPurchase", "Verified"
            });
    }

    [Fact]
    public void UpdateReviewRequest_cannot_supply_status_or_product()
    {
        // Editing content must not be a back door to publishing, re-pointing, or re-owning a
        // review: only SetStatus and the moderation handler may change those.
        typeof(UpdateReviewRequest)
            .GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(new[]
            {
                "Status", "PublishedAtUtc", "ProductId", "ProductVariantId",
                "CustomerId", "IsVerifiedPurchase", "HelpfulCount"
            });
    }

    [Fact]
    public void ModerateReviewRequest_cannot_supply_a_customer_id()
    {
        // Moderation acts on a review id, never on a person.
        typeof(ModerateReviewRequest)
            .GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(new[] { "CustomerId", "ProductId" });
    }

    [Fact]
    public void Public_review_response_never_exposes_customer_identity()
    {
        // Reviews are public content. The admin projection carries the author; the public one
        // must not, or a storefront response becomes an account-enumeration surface.
        typeof(ProductReviewSummaryResponse)
            .GetProperties()
            .Select(p => p.Name)
            .Should().NotContain(new[]
            {
                "CustomerId", "CustomerEmail", "Email", "Phone", "PhoneNumber"
            });
    }

    [Fact]
    public void Public_review_response_does_expose_a_display_name()
    {
        // Counterpart to the assertion above, so the contract cannot be satisfied by simply
        // deleting the field and breaking rendering.
        typeof(ProductReviewSummaryResponse)
            .GetProperty("AuthorName")
            .Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // Review image origin validation
    // -----------------------------------------------------------------------

    private static ReviewImageRequest Image(string publicId = "reviews/a", string url = "https://res.cloudinary.com/demo/image/upload/reviews/a.jpg")
        => new(publicId, url);

    [Fact]
    public void Image_accepts_a_cloudinary_url() =>
        ReviewImageFactory.Build([Image()]).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("https://evil.example.com/x.jpg")]
    [InlineData("http://res.cloudinary.com/demo/image/upload/x.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://res.cloudinary.com.evil.example/x.jpg")]
    public void Image_rejects_a_url_that_is_not_a_cloudinary_https_url(string url)
    {
        // Review images are rendered by every other customer, so a tampered client must not be
        // able to persist an arbitrary or javascript: URL into public content.
        var result = ReviewImageFactory.Build([Image(url: url)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("REVIEW_IMAGE_INVALID");
    }

    [Theory]
    [InlineData(null, "https://res.cloudinary.com/demo/x.jpg")]
    [InlineData("reviews/a", null)]
    [InlineData("", "https://res.cloudinary.com/demo/x.jpg")]
    [InlineData("reviews/a", "")]
    public void Image_requires_both_a_public_id_and_a_url(string? publicId, string? url)
    {
        var result = ReviewImageFactory.Build([new ReviewImageRequest(publicId!, url!)]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("REVIEW_IMAGE_INVALID");
    }

    [Fact]
    public void Null_image_list_yields_no_images_rather_than_failing() =>
        ReviewImageFactory.Build(null).IsSuccess.Should().BeTrue();

    [Fact]
    public void Empty_image_list_yields_no_images() =>
        ReviewImageFactory.Build([]).Value.Should().BeEmpty();

    // -----------------------------------------------------------------------
    // Author display name
    // -----------------------------------------------------------------------

    [Fact]
    public void AuthorName_joins_first_and_last() =>
        ReviewMapper.AuthorName("Ada", "Lovelace", "ada@example.test").Should().Be("Ada Lovelace");

    [Fact]
    public void AuthorName_falls_back_to_the_email_local_part()
    {
        // A row must never render as a blank author.
        ReviewMapper.AuthorName(null, null, "ada@example.test").Should().Be("ada");
        ReviewMapper.AuthorName("", "", "ada@example.test").Should().Be("ada");
    }

    [Fact]
    public void AuthorName_falls_back_to_a_generic_label_when_there_is_nothing_else()
    {
        ReviewMapper.AuthorName(null, null, null).Should().Be("Customer");
        ReviewMapper.AuthorName(null, null, "not-an-email").Should().Be("Customer");
    }

    [Fact]
    public void AuthorName_never_returns_the_full_email()
    {
        // The domain must never leak into the public review response.
        ReviewMapper.AuthorName(null, null, "ada.lovelace@example.test")
            .Should().NotContain("@");
    }

    // -----------------------------------------------------------------------
    // The status enum is persisted by name, so its values are part of the schema
    // -----------------------------------------------------------------------

    [Fact]
    public void Review_status_values_are_the_documented_names()
    {
        Enum.GetNames<ReviewStatus>()
            .Should().BeEquivalentTo(["Pending", "Published", "Rejected"]);
    }

    [Fact]
    public void Image_limit_matches_the_contract()
    {
        // A single source of truth for "how many photos may a review carry".
        typeof(ProductReview).GetField(nameof(ProductReview.MaxImages))
            .Should().NotBeNull();
        ProductReview.MaxImages.Should().Be(3);
    }
}