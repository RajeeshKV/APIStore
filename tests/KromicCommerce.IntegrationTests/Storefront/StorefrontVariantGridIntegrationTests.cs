using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Application.Features.Catalog.Products.Variants;
using KromicCommerce.Application.Features.Storefront.Products.GetStorefrontVariantGrid;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Domain.Catalog;
using KromicCommerce.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace KromicCommerce.IntegrationTests.Storefront;

[Collection("Database")]
public sealed class StorefrontVariantGridIntegrationTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    [SkippableFact]
    public async Task A_product_with_two_variants_returns_two_rows()
    {
        var category = await SeedCategoryAsync();
        var brand = await SeedBrandAsync();
        var product = await SeedProductAsync(category.Id, brand.Id);

        try
        {
            var attr = await SeedAttributeAsync(product.Id, "Colour");
            var red = await SeedAttributeValueAsync(attr.Id, "Red");
            var blue = await SeedAttributeValueAsync(attr.Id, "Blue");

            await SeedVariantAsync(product.Id, [red], sku: "TSH-RED", priceOverride: 999m);
            await SeedVariantAsync(product.Id, [blue], sku: "TSH-BLU", priceOverride: 1099m);

            await using var ctx = Db.CreateDbContext();
            var handler = new GetStorefrontVariantGridHandler(ctx, MockBusinessSettings(), NullLogger<GetStorefrontVariantGridHandler>.Instance);

            var result = await handler.Handle(
                new GetStorefrontVariantGridQuery(new StorefrontProductQueryRequest()),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Items.Should().HaveCount(2);

            var skus = result.Value.Items.Select(i => i.Sku).OrderBy(s => s).ToList();
            skus.Should().Equal(["TSH-BLU", "TSH-RED"]);
            result.Value.Items.Should().AllSatisfy(i =>
            {
                i.ProductId.Should().Be(product.Id);
                i.EffectivePrice.Should().BeOneOf(999m, 1099m);
                i.CanPurchase.Should().BeFalse("no stock was set, so the variant is out of stock");
                i.StockAvailability.Should().Be(StockAvailability.OutOfStock);
                i.IsOutOfStock.Should().BeTrue();
            });
        }
        finally
        {
            await CleanupProductAsync(product.Id);
        }
    }

    [SkippableFact]
    public async Task A_product_without_variants_appears_once_with_null_variant_id()
    {
        var category = await SeedCategoryAsync();
        var brand = await SeedBrandAsync();
        var product = await SeedProductAsync(category.Id, brand.Id);

        try
        {
            await using var ctx = Db.CreateDbContext();
            var handler = new GetStorefrontVariantGridHandler(ctx, MockBusinessSettings(), NullLogger<GetStorefrontVariantGridHandler>.Instance);

            var result = await handler.Handle(
                new GetStorefrontVariantGridQuery(new StorefrontProductQueryRequest()),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Items.Should().Contain(i => i.VariantId == null && i.ProductId == product.Id);
        }
        finally
        {
            await CleanupProductAsync(product.Id);
        }
    }

    [SkippableFact]
    public async Task Inactive_variants_are_excluded()
    {
        var category = await SeedCategoryAsync();
        var brand = await SeedBrandAsync();
        var product = await SeedProductAsync(category.Id, brand.Id);

        try
        {
            var attr = await SeedAttributeAsync(product.Id, "Colour");
            var red = await SeedAttributeValueAsync(attr.Id, "Red");
            await SeedVariantAsync(product.Id, [red], sku: "TSH-RED", priceOverride: 999m, isActive: true);
            await SeedVariantAsync(product.Id, [red], sku: "TSH-RED-X", priceOverride: 999m, isActive: false);

            await using var ctx = Db.CreateDbContext();
            var handler = new GetStorefrontVariantGridHandler(ctx, MockBusinessSettings(), NullLogger<GetStorefrontVariantGridHandler>.Instance);

            var result = await handler.Handle(
                new GetStorefrontVariantGridQuery(new StorefrontProductQueryRequest()),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Items.Should().HaveCount(1);
            result.Value.Items[0].Sku.Should().Be("TSH-RED");
        }
        finally
        {
            await CleanupProductAsync(product.Id);
        }
    }

    [SkippableFact]
    public async Task Setting_stock_makes_a_variant_purchasable()
    {
        var category = await SeedCategoryAsync();
        var brand = await SeedBrandAsync();
        var product = await SeedProductAsync(category.Id, brand.Id);

        try
        {
            var attr = await SeedAttributeAsync(product.Id, "Colour");
            var red = await SeedAttributeValueAsync(attr.Id, "Red");
            var variantId = await SeedVariantAsync(product.Id, [red], sku: "TSH-RED", priceOverride: 999m);

            await using (var invCtx = Db.CreateDbContext())
            {
                var inv = InventoryItem.Create(product.Id, variantId, 6, 5);
                invCtx.InventoryItems.Add(inv);
                await invCtx.SaveChangesAsync();
            }

            await using var ctx = Db.CreateDbContext();
            var handler = new GetStorefrontVariantGridHandler(ctx, MockBusinessSettings(), NullLogger<GetStorefrontVariantGridHandler>.Instance);

            var result = await handler.Handle(
                new GetStorefrontVariantGridQuery(new StorefrontProductQueryRequest()),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Items.Should().HaveCount(1);
            result.Value.Items[0].StockAvailability.Should().Be(StockAvailability.InStock);
            result.Value.Items[0].CanPurchase.Should().BeTrue();
            result.Value.Items[0].IsOutOfStock.Should().BeFalse();
        }
        finally
        {
            await CleanupProductAsync(product.Id);
        }
    }

    [SkippableFact]
    public async Task Category_filter_limits_the_grid()
    {
        var catShirts = await SeedCategoryAsync(name: "Shirts");
        var catPants = await SeedCategoryAsync(name: "Pants");
        var brand = await SeedBrandAsync();
        var shirtProduct = await SeedProductAsync(catShirts.Id, brand.Id, name: "Red Shirt");
        var pantsProduct = await SeedProductAsync(catPants.Id, brand.Id, name: "Blue Pants");

        try
        {
            await using var ctx = Db.CreateDbContext();
            var handler = new GetStorefrontVariantGridHandler(ctx, MockBusinessSettings(), NullLogger<GetStorefrontVariantGridHandler>.Instance);

            var result = await handler.Handle(
                new GetStorefrontVariantGridQuery(new StorefrontProductQueryRequest
                {
                    CategorySlug = catShirts.Slug.ToLower()
                }),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Items.Should().OnlyContain(i => i.CategorySlug == catShirts.Slug.ToLower());
        }
        finally
        {
            await CleanupProductAsync(shirtProduct.Id);
            await CleanupProductAsync(pantsProduct.Id);
        }
    }

    [SkippableFact]
    public async Task Mixed_catalog_returns_variants_and_simple_products_in_one_total_count()
    {
        var category = await SeedCategoryAsync();
        var brand = await SeedBrandAsync();

        // 1 product with 2 variants
        var variantProduct = await SeedProductAsync(category.Id, brand.Id, name: "T-Shirt");
        var attr = await SeedAttributeAsync(variantProduct.Id, "Colour");
        var red = await SeedAttributeValueAsync(attr.Id, "Red");
        var blue = await SeedAttributeValueAsync(attr.Id, "Blue");
        await SeedVariantAsync(variantProduct.Id, [red], sku: "TSH-RED");
        await SeedVariantAsync(variantProduct.Id, [blue], sku: "TSH-BLU");

        // 2 products without variants
        var simple1 = await SeedProductAsync(category.Id, brand.Id, name: "Gift Card");
        var simple2 = await SeedProductAsync(category.Id, brand.Id, name: "Sticker");

        try
        {
            await using var ctx = Db.CreateDbContext();
            var handler = new GetStorefrontVariantGridHandler(ctx, MockBusinessSettings(), NullLogger<GetStorefrontVariantGridHandler>.Instance);

            var result = await handler.Handle(
                    new GetStorefrontVariantGridQuery(new StorefrontProductQueryRequest(PageSize: 100)),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();

            // 2 variants + 2 simple products = 4 rows
            result.Value.Items.Should().HaveCount(4);
            result.Value.TotalCount.Should().Be(4, "totalCount must include fallback rows for simple products");

            var simpleRows = result.Value.Items.Where(i => i.VariantId == null).ToList();
            simpleRows.Should().HaveCount(2);
            simpleRows.Select(i => i.Name).Should().Contain(new[] { "Gift Card", "Sticker" });

            var variantRows = result.Value.Items.Where(i => i.VariantId != null).ToList();
            variantRows.Should().HaveCount(2);
            variantRows.Select(i => i.Sku).Should().Contain(new[] { "TSH-RED", "TSH-BLU" });
        }
        finally
        {
            await CleanupProductAsync(variantProduct.Id);
            await CleanupProductAsync(simple1.Id);
            await CleanupProductAsync(simple2.Id);
        }
    }

    [SkippableFact]
    public async Task Search_filters_by_product_name()
    {
        var category = await SeedCategoryAsync();
        var brand = await SeedBrandAsync();
        var product1 = await SeedProductAsync(category.Id, brand.Id, name: "Mega T-Shirt");
        var product2 = await SeedProductAsync(category.Id, brand.Id, name: "Cool Hoodie");

        try
        {
            await using var ctx = Db.CreateDbContext();
            var handler = new GetStorefrontVariantGridHandler(ctx, MockBusinessSettings(), NullLogger<GetStorefrontVariantGridHandler>.Instance);

            var result = await handler.Handle(
                new GetStorefrontVariantGridQuery(new StorefrontProductQueryRequest
                {
                    Search = "t-shirt"
                }),
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Items.Should().OnlyContain(i => i.Name.Contains("T-Shirt"));
        }
        finally
        {
            await CleanupProductAsync(product1.Id);
            await CleanupProductAsync(product2.Id);
        }
    }

    // -----------------------------------------------------------------------
    // Seeds
    // -----------------------------------------------------------------------

    private async Task<Category> SeedCategoryAsync(string? name = null, string? slug = null)
    {
        await using var ctx = Db.CreateDbContext();
        var cat = Category.Create(
            name ?? "cat-" + Guid.NewGuid().ToString("N")[..8],
            slug ?? name?.ToLower() ?? "slug-" + Guid.NewGuid().ToString("N")[..8],
            null, null);
        ctx.Categories.Add(cat);
        await ctx.SaveChangesAsync();
        return cat;
    }

    private async Task<Brand> SeedBrandAsync(string? name = null, string? slug = null)
    {
        await using var ctx = Db.CreateDbContext();
        var brand = Brand.Create(
            name ?? "brand-" + Guid.NewGuid().ToString("N")[..8],
            slug ?? name?.ToLower() ?? "slug-" + Guid.NewGuid().ToString("N")[..8],
            null, null);
        ctx.Brands.Add(brand);
        await ctx.SaveChangesAsync();
        return brand;
    }

    private async Task<Product> SeedProductAsync(Guid categoryId, Guid brandId, string? name = null)
    {
        await using var ctx = Db.CreateDbContext();
        var product = Product.Create(
            name ?? "prod-" + Guid.NewGuid().ToString("N")[..8],
            "slug-" + Guid.NewGuid().ToString("N")[..8],
            null, 499m, categoryId, brandId);
        product.Publish();
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product;
    }

    private async Task<ProductAttribute> SeedAttributeAsync(Guid productId, string? name = null)
    {
        await using var ctx = Db.CreateDbContext();
        var attr = ProductAttribute.Create(productId, name ?? "attr-" + Guid.NewGuid().ToString("N")[..8]);
        ctx.ProductAttributes.Add(attr);
        await ctx.SaveChangesAsync();
        return attr;
    }

    private async Task<Guid> SeedAttributeValueAsync(Guid attributeId, string value)
    {
        await using var ctx = Db.CreateDbContext();
        var av = ProductAttributeValue.Create(attributeId, value);
        ctx.ProductAttributeValues.Add(av);
        await ctx.SaveChangesAsync();
        return av.Id;
    }

    private async Task<Guid> SeedVariantAsync(Guid productId, Guid[] attributeValueIds, string? sku = null, decimal? priceOverride = null, decimal? compareAtPrice = null, bool isActive = true)
    {
        await using var ctx = Db.CreateDbContext();
        var variant = ProductVariant.Create(productId, sku, priceOverride, compareAtPrice);
        variant.SetAttributeValues(attributeValueIds);
        if (!isActive) variant.Deactivate();
        ctx.ProductVariants.Add(variant);
        await ctx.SaveChangesAsync();
        return variant.Id;
    }

    private static IBusinessSettingsService MockBusinessSettings()
    {
        var settings = BusinessSettings.CreateDefault("Test Store");
        var mock = new Mock<IBusinessSettingsService>();
        mock.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
        return mock.Object;
    }

    private async Task CleanupProductAsync(Guid productId)
    {
        await using var ctx = Db.CreateDbContext();
        
        var variants = await ctx.ProductVariants
            .Where(v => v.ProductId == productId)
            .ToListAsync();
        ctx.ProductVariants.RemoveRange(variants);
        
        var productImages = await ctx.ProductImages
            .Where(i => i.ProductId == productId)
            .ToListAsync();
        ctx.ProductImages.RemoveRange(productImages);
        
        var attributes = await ctx.ProductAttributes
            .Where(a => a.ProductId == productId)
            .Include(a => a.Values)
            .ToListAsync();
        foreach (var attr in attributes)
        {
            ctx.ProductAttributeValues.RemoveRange(attr.Values);
        }
        ctx.ProductAttributes.RemoveRange(attributes);
        
        var product = await ctx.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product != null)
            ctx.Products.Remove(product);
        
        await ctx.SaveChangesAsync();
    }
}
