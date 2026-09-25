using KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProducts;

namespace KromicCommerce.UnitTests.Application;

public sealed class StorefrontProductQueryValidatorTests
{
    private readonly GetStorefrontProductsValidator _validator = new();

    private static GetStorefrontProductsQuery Valid() =>
        new(new StorefrontProductQueryRequest());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_page_less_than_1(int page)
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(Page: page)
        });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Rejects_invalid_page_size(int pageSize)
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(PageSize: pageSize)
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_search_longer_than_200_chars()
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(Search: new string('x', 201))
        });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("sku")]
    [InlineData("'; DROP TABLE products;--")]
    public void Rejects_invalid_sort_field(string sortBy)
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(SortBy: sortBy)
        });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("price")]
    [InlineData("created_at")]
    [InlineData(null)]
    public void Accepts_valid_sort_fields(string? sortBy)
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(SortBy: sortBy)
        });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    [InlineData("ASC")]
    [InlineData("DESC")]
    public void Accepts_valid_sort_direction(string dir)
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(SortDirection: dir)
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_invalid_sort_direction()
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(SortDirection: "random")
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_negative_min_price()
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(MinPrice: -1m)
        });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Accepts_zero_min_price()
    {
        var result = _validator.Validate(Valid() with
        {
            Request = new StorefrontProductQueryRequest(MinPrice: 0m)
        });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Default_query_is_valid()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }
}
