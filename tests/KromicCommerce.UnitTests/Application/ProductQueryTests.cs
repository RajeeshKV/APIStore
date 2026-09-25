using KromicCommerce.Application.Features.Catalog.Products.GetProducts;

namespace KromicCommerce.UnitTests.Application;

public sealed class ProductQueryTests
{
    [Theory]
    [InlineData("name")]
    [InlineData("price")]
    [InlineData("created_at")]
    [InlineData("updated_at")]
    [InlineData(null)]
    public void Valid_sort_fields_are_accepted(string? sortBy)
    {
        ProductSortFields.IsValid(sortBy).Should().BeTrue();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("category_id")]
    [InlineData("'; DROP TABLE products;--")]
    [InlineData("1=1")]
    public void Invalid_sort_fields_are_rejected(string sortBy)
    {
        ProductSortFields.IsValid(sortBy).Should().BeFalse();
    }

    [Fact]
    public void PagedResponse_computes_TotalPages_correctly()
    {
        var paged = new KromicCommerce.Contracts.Common.PagedResponse<int>(
            Items: [1, 2, 3],
            Page: 1,
            PageSize: 20,
            TotalCount: 45);

        paged.TotalPages.Should().Be(3);     // ceil(45/20) = 3
        paged.HasNextPage.Should().BeTrue();
        paged.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResponse_last_page_has_no_next()
    {
        var paged = new KromicCommerce.Contracts.Common.PagedResponse<int>(
            Items: [1],
            Page: 3,
            PageSize: 20,
            TotalCount: 45);

        paged.HasNextPage.Should().BeFalse();
        paged.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void PagedResponse_empty_result_returns_zero_pages()
    {
        var paged = new KromicCommerce.Contracts.Common.PagedResponse<int>(
            Items: [],
            Page: 1,
            PageSize: 20,
            TotalCount: 0);

        paged.TotalPages.Should().Be(0);
        paged.HasNextPage.Should().BeFalse();
        paged.HasPreviousPage.Should().BeFalse();
    }
}
