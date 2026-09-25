namespace KromicCommerce.UnitTests.Domain;

public sealed class StorePolicyTests
{
    // -----------------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_sets_type_title_content_and_unpublished()
    {
        var policy = StorePolicy.Create(PolicyType.TermsConditions, "Terms", "Our terms content.");

        policy.PolicyType.Should().Be(PolicyType.TermsConditions);
        policy.Title.Should().Be("Terms");
        policy.Content.Should().Be("Our terms content.");
        policy.IsPublished.Should().BeFalse();
    }

    [Fact]
    public void Create_trims_title_and_content()
    {
        var policy = StorePolicy.Create(PolicyType.PrivacyPolicy, "  Privacy  ", "  Content  ");

        policy.Title.Should().Be("Privacy");
        policy.Content.Should().Be("Content");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_for_empty_title(string title)
    {
        var act = () => StorePolicy.Create(PolicyType.PrivacyPolicy, title, "Content");
        act.Should().Throw<ArgumentException>().WithMessage("*title*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_for_empty_content(string content)
    {
        var act = () => StorePolicy.Create(PolicyType.PrivacyPolicy, "Title", content);
        act.Should().Throw<ArgumentException>().WithMessage("*content*");
    }

    [Theory]
    [InlineData(PolicyType.TermsConditions)]
    [InlineData(PolicyType.PrivacyPolicy)]
    [InlineData(PolicyType.RefundPolicy)]
    [InlineData(PolicyType.ShippingPolicy)]
    [InlineData(PolicyType.CancellationPolicy)]
    [InlineData(PolicyType.ReturnPolicy)]
    [InlineData(PolicyType.OrderPolicy)]
    public void Create_accepts_all_policy_types(PolicyType type)
    {
        var policy = StorePolicy.Create(type, "Title", "Content");
        policy.PolicyType.Should().Be(type);
    }

    // -----------------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------------

    [Fact]
    public void Update_replaces_title_and_content()
    {
        var policy = StorePolicy.Create(PolicyType.RefundPolicy, "Old Title", "Old Content");

        policy.Update("New Title", "New Content");

        policy.Title.Should().Be("New Title");
        policy.Content.Should().Be("New Content");
    }

    [Fact]
    public void Update_trims_inputs()
    {
        var policy = StorePolicy.Create(PolicyType.RefundPolicy, "Title", "Content");
        policy.Update("  Updated  ", "  Body  ");
        policy.Title.Should().Be("Updated");
        policy.Content.Should().Be("Body");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_throws_for_empty_title(string title)
    {
        var policy = StorePolicy.Create(PolicyType.PrivacyPolicy, "Title", "Content");
        var act = () => policy.Update(title, "Content");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_throws_for_empty_content(string content)
    {
        var policy = StorePolicy.Create(PolicyType.PrivacyPolicy, "Title", "Content");
        var act = () => policy.Update("Title", content);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_does_not_change_published_state()
    {
        var policy = StorePolicy.Create(PolicyType.ShippingPolicy, "Title", "Content");
        policy.Publish();
        policy.Update("New Title", "New Content");
        policy.IsPublished.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Publish / Unpublish
    // -----------------------------------------------------------------------

    [Fact]
    public void Publish_sets_IsPublished_true()
    {
        var policy = StorePolicy.Create(PolicyType.ReturnPolicy, "Returns", "We accept returns.");
        policy.IsPublished.Should().BeFalse();

        policy.Publish();

        policy.IsPublished.Should().BeTrue();
    }

    [Fact]
    public void Unpublish_sets_IsPublished_false()
    {
        var policy = StorePolicy.Create(PolicyType.OrderPolicy, "Orders", "Order policy.");
        policy.Publish();
        policy.Unpublish();

        policy.IsPublished.Should().BeFalse();
    }

    [Fact]
    public void Publish_is_idempotent()
    {
        var policy = StorePolicy.Create(PolicyType.TermsConditions, "Terms", "Content");
        policy.Publish();
        policy.Publish();
        policy.IsPublished.Should().BeTrue();
    }

    [Fact]
    public void Unpublish_is_idempotent()
    {
        var policy = StorePolicy.Create(PolicyType.TermsConditions, "Terms", "Content");
        policy.Unpublish();
        policy.Unpublish();
        policy.IsPublished.Should().BeFalse();
    }
}
