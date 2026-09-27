using KromicCommerce.Application.Features.Catalog.Products.Variants;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Tests for VariantAttributeHelper — the duplicate-combination and attribute-validation logic.
/// These tests focus on the pure algorithmic parts that can be verified without a DB.
/// </summary>
public sealed class VariantAttributeHelperTests
{
    // -----------------------------------------------------------------------
    // SortedIds — order-insensitive canonical key
    // -----------------------------------------------------------------------

    [Fact]
    public void SortedIds_returns_deterministic_order()
    {
        var a = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000000");
        var b = Guid.Parse("BBBBBBBB-0000-0000-0000-000000000000");
        var c = Guid.Parse("CCCCCCCC-0000-0000-0000-000000000000");

        var sorted = VariantAttributeHelper.SortedIds([c, a, b]).ToList();
        sorted.Should().Equal(a, b, c);
    }

    [Fact]
    public void SortedIds_same_input_different_order_produces_same_result()
    {
        var id1 = Guid.Parse("11111111-0000-0000-0000-000000000000");
        var id2 = Guid.Parse("22222222-0000-0000-0000-000000000000");

        var forward = string.Join(",", VariantAttributeHelper.SortedIds([id1, id2]));
        var backward = string.Join(",", VariantAttributeHelper.SortedIds([id2, id1]));
        forward.Should().Be(backward);
    }

    // -----------------------------------------------------------------------
    // Duplicate combination detection (algorithmic, in-memory)
    // The full DB-backed version is covered by the handler tests.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(
        "AAAAAAAA-0000-0000-0000-000000000001,AAAAAAAA-0000-0000-0000-000000000002",
        "AAAAAAAA-0000-0000-0000-000000000001,AAAAAAAA-0000-0000-0000-000000000002",
        true)]
    [InlineData(
        "AAAAAAAA-0000-0000-0000-000000000001,AAAAAAAA-0000-0000-0000-000000000002",
        "AAAAAAAA-0000-0000-0000-000000000002,AAAAAAAA-0000-0000-0000-000000000001",
        true)]   // reversed order — still duplicate
    [InlineData(
        "AAAAAAAA-0000-0000-0000-000000000001,AAAAAAAA-0000-0000-0000-000000000002",
        "AAAAAAAA-0000-0000-0000-000000000001,AAAAAAAA-0000-0000-0000-000000000003",
        false)]  // different second value — not a duplicate
    public void Canonical_key_detects_duplicates_correctly(
        string storedCsv, string incomingCsv, bool shouldMatch)
    {
        // Build canonical keys directly (mirrors CheckDuplicateCombination logic)
        static string Canonical(string csv) => string.Join(",",
            csv.Split(',').Select(Guid.Parse).OrderBy(g => g));

        var stored = Canonical(storedCsv);
        var incoming = Canonical(incomingCsv);

        if (shouldMatch)
            stored.Should().Be(incoming);
        else
            stored.Should().NotBe(incoming);
    }

    [Fact]
    public void Single_attribute_value_is_not_a_duplicate_of_different_single_value()
    {
        var id1 = Guid.Parse("11111111-0000-0000-0000-000000000000");
        var id2 = Guid.Parse("22222222-0000-0000-0000-000000000000");

        var c1 = string.Join(",", VariantAttributeHelper.SortedIds([id1]));
        var c2 = string.Join(",", VariantAttributeHelper.SortedIds([id2]));

        c1.Should().NotBe(c2);
    }

    [Fact]
    public void Empty_attribute_list_produces_empty_canonical_key()
    {
        var result = string.Join(",", VariantAttributeHelper.SortedIds([]));
        result.Should().Be(string.Empty);
    }
}
