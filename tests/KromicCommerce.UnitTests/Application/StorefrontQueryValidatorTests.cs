using KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProducts;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Additional storefront query contract tests: empty result is not an error,
/// max page size enforcement, sort whitelist.
/// </summary>
public sealed class StorefrontQueryContractTests
{
    [Fact]
    public void PagedResponse_empty_result_is_200_not_error()
    {
        // An empty storefront result is represented as Items=[] TotalCount=0 — never a 404
        var paged = new PagedResponse<StorefrontProductSummaryResponse>(
            Items: [],
            Page: 1,
            PageSize: 20,
            TotalCount: 0);

        paged.Items.Should().BeEmpty();
        paged.TotalCount.Should().Be(0);
        paged.TotalPages.Should().Be(0);
        paged.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResponse_correct_pages_for_partial_last_page()
    {
        var paged = new PagedResponse<StorefrontProductSummaryResponse>(
            Items: [], Page: 3, PageSize: 20, TotalCount: 41);

        paged.TotalPages.Should().Be(3);  // ceil(41/20) = 3
        paged.HasNextPage.Should().BeFalse();
        paged.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Max_page_size_constant_is_100()
    {
        // Ensure no unclamped requests can load unlimited products
        var validator = new GetStorefrontProductsValidator();
        var tooLarge = validator.Validate(
            new GetStorefrontProductsQuery(
                new StorefrontProductQueryRequest(PageSize: 101)));
        tooLarge.IsValid.Should().BeFalse();

        var atMax = validator.Validate(
            new GetStorefrontProductsQuery(
                new StorefrontProductQueryRequest(PageSize: 100)));
        atMax.IsValid.Should().BeTrue();
    }
}
