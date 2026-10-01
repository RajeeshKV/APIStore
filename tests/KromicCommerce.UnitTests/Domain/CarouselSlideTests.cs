using KromicCommerce.Domain.Catalog;

namespace KromicCommerce.UnitTests.Domain;

/// <summary>Carousel slide domain rules, including the CTA-destination guarantee at entity level.</summary>
public sealed class CarouselSlideTests
{
    [Fact]
    public void Create_requires_a_title()
    {
        var act = () => CarouselSlide.Create("  ", null, null, 0);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_trims_text_and_preserves_optional_fields_as_null()
    {
        var slide = CarouselSlide.Create("  Summer Sale  ", null, null, 3, isActive: true);

        slide.Title.Should().Be("Summer Sale");
        slide.Subtitle.Should().BeNull();
        slide.CtaText.Should().BeNull();
        slide.SortOrder.Should().Be(3);
        slide.IsActive.Should().BeTrue();
        slide.ImageUrl.Should().BeNull();
    }

    [Fact]
    public void A_new_slide_starts_with_no_image()
    {
        // The image is uploaded afterwards, so a slide begins as a draft.
        CarouselSlide.Create("Sale", null, null, 0).ImageUrl.Should().BeNull();
    }

    [Fact]
    public void Update_requires_a_title()
    {
        var slide = CarouselSlide.Create("Sale", null, null, 0);

        var act = () => slide.Update("   ", null, null, 0, false);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_does_not_disturb_the_image()
    {
        // Text edits and image uploads are separate operations; changing one must not clear the
        // other or the slide would silently vanish from the storefront.
        var slide = CarouselSlide.Create("Sale", null, null, 0, isActive: true);
        slide.SetImage("carousel/a", "https://cdn/a.jpg");

        slide.Update("Sale v2", "New", "Shop", 1, false);

        slide.ImageUrl.Should().Be("https://cdn/a.jpg");
        slide.ImagePublicId.Should().Be("carousel/a");
        slide.Title.Should().Be("Sale v2");
        slide.SortOrder.Should().Be(1);
        slide.IsActive.Should().BeFalse();
    }

    [Fact]
    public void SetImage_stores_the_reference()
    {
        var slide = CarouselSlide.Create("Sale", null, null, 0);

        slide.SetImage("  carousel/a  ", "  https://cdn/a.jpg  ");

        slide.ImagePublicId.Should().Be("carousel/a");
        slide.ImageUrl.Should().Be("https://cdn/a.jpg");
    }

    [Theory]
    [InlineData("", "https://cdn/a.jpg")]
    [InlineData("carousel/a", "")]
    [InlineData("  ", "  ")]
    public void SetImage_rejects_a_blank_reference(string publicId, string url)
    {
        var slide = CarouselSlide.Create("Sale", null, null, 0);

        var act = () => slide.SetImage(publicId, url);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ClearImage_returns_the_slide_to_draft_without_deleting_it()
    {
        var slide = CarouselSlide.Create("Sale", null, null, 0, isActive: true);
        slide.SetImage("carousel/a", "https://cdn/a.jpg");

        slide.ClearImage();

        slide.ImageUrl.Should().BeNull();
        slide.ImagePublicId.Should().BeNull();
        slide.Title.Should().Be("Sale");
    }

    [Fact]
    public void Activate_and_deactivate_toggle_visibility()
    {
        var slide = CarouselSlide.Create("Sale", null, null, 0);

        slide.Activate();
        slide.IsActive.Should().BeTrue();

        slide.Deactivate();
        slide.IsActive.Should().BeFalse();
    }

    [Fact]
    public void The_entity_exposes_no_cta_destination_field()
    {
        // Structural guarantee that a slide can never carry an arbitrary link: there is no
        // property anywhere on the entity to store one, so no handler can read one back out.
        typeof(CarouselSlide)
            .GetProperties()
            .Select(p => p.Name)
            .Should().NotContain("CtaUrl")
            .And.NotContain("CtaTarget")
            .And.NotContain("Link")
            .And.NotContain("Url")
            .And.NotContain("RedirectUrl");
    }

    [Fact]
    public void The_entity_exposes_no_tenant_or_store_column()
    {
        // This deployment is single-tenant: one store per deployment, one database. There is
        // deliberately no StoreId/TenantId to filter by.
        typeof(CarouselSlide)
            .GetProperties()
            .Select(p => p.Name)
            .Should().NotContain("TenantId")
            .And.NotContain("StoreId");
    }
}