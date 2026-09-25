using Microsoft.AspNetCore.DataProtection;
using KromicCommerce.Infrastructure.Security;

namespace KromicCommerce.UnitTests.Infrastructure;

/// <summary>
/// Tests for DataProtectionSecretService.
/// Uses EphemeralDataProtectionProvider so no key material is persisted to disk.
/// The ephemeral provider is scoped to each test instance.
/// </summary>
public sealed class DataProtectionSecretServiceTests
{
    private static DataProtectionSecretService CreateSut()
    {
        // EphemeralDataProtectionProvider generates a new key ring in memory each run.
        // This validates that Protect/Unprotect round-trips correctly without any
        // file system or environment dependency.
        var provider = new EphemeralDataProtectionProvider();
        return new DataProtectionSecretService(provider);
    }

    // -----------------------------------------------------------------------
    // Protect
    // -----------------------------------------------------------------------

    [Fact]
    public void Protect_returns_non_empty_ciphertext()
    {
        var sut = CreateSut();
        var result = sut.Protect("my-secret-key");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Protect_ciphertext_differs_from_plaintext()
    {
        var sut = CreateSut();
        var result = sut.Protect("my-secret-key");
        result.Should().NotBe("my-secret-key");
    }

    [Fact]
    public void Protect_produces_different_ciphertext_for_same_plaintext_each_call()
    {
        // Data Protection adds nonce — two calls with same input should yield different ciphertext.
        var sut = CreateSut();
        var first = sut.Protect("same-secret");
        var second = sut.Protect("same-secret");
        first.Should().NotBe(second);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Protect_throws_for_null_or_empty_plaintext(string? input)
    {
        var sut = CreateSut();
        var act = () => sut.Protect(input!);
        act.Should().Throw<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // Unprotect
    // -----------------------------------------------------------------------

    [Fact]
    public void Unprotect_round_trips_correctly()
    {
        var sut = CreateSut();
        var original = "rzp_live_supersecret";

        var ciphertext = sut.Protect(original);
        var recovered = sut.Unprotect(ciphertext);

        recovered.Should().Be(original);
    }

    [Theory]
    [InlineData("api-key-value")]
    [InlineData("sk_test_abc123xyz")]
    [InlineData("A very long secret with special chars: !@#$%^&*()")]
    public void Unprotect_recovers_various_secrets(string secret)
    {
        var sut = CreateSut();
        var ciphertext = sut.Protect(secret);
        sut.Unprotect(ciphertext).Should().Be(secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Unprotect_throws_for_null_or_empty_ciphertext(string? input)
    {
        var sut = CreateSut();
        var act = () => sut.Unprotect(input!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unprotect_throws_for_tampered_ciphertext()
    {
        var sut = CreateSut();
        var ciphertext = sut.Protect("original-secret");
        var tampered = ciphertext[..^4] + "XXXX"; // corrupt last 4 chars

        var act = () => sut.Unprotect(tampered);
        act.Should().Throw<Exception>(); // CryptographicException from Data Protection
    }

    // -----------------------------------------------------------------------
    // Mask — secrets must never be fully exposed
    // -----------------------------------------------------------------------

    [Fact]
    public void Mask_returns_four_stars_for_null()
    {
        var sut = CreateSut();
        sut.Mask(null).Should().Be("****");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Mask_returns_four_stars_for_empty_or_whitespace(string? input)
    {
        var sut = CreateSut();
        sut.Mask(input).Should().Be("****");
    }

    [Fact]
    public void Mask_shows_last_four_chars_by_default()
    {
        var sut = CreateSut();
        sut.Mask("abc123456789").Should().Be("****6789");
    }

    [Fact]
    public void Mask_uses_custom_visible_suffix_length()
    {
        var sut = CreateSut();
        sut.Mask("abc123456789", visibleSuffix: 6).Should().Be("****456789");
    }

    [Fact]
    public void Mask_returns_all_stars_when_plaintext_shorter_than_visible_suffix()
    {
        var sut = CreateSut();
        // "abc" is 3 chars, visibleSuffix defaults to 4 — mask all
        sut.Mask("abc").Should().Be("***");
    }

    [Fact]
    public void Mask_returns_all_stars_when_plaintext_equals_visible_suffix()
    {
        var sut = CreateSut();
        sut.Mask("abcd", visibleSuffix: 4).Should().Be("****");
    }

    [Fact]
    public void Mask_never_returns_full_plaintext()
    {
        var sut = CreateSut();
        var secret = "rzp_live_supersecretkey";
        sut.Mask(secret).Should().NotBe(secret);
        sut.Mask(secret).Should().StartWith("****");
    }

    [Fact]
    public void Mask_is_safe_to_call_on_unprotected_values_without_decryption()
    {
        // Mask operates on plaintext only — no Unprotect involved.
        // Confirms the masking path never requires decryption (safe for logging).
        var sut = CreateSut();
        var ciphertext = sut.Protect("secret");
        // Masking ciphertext (as if it were plaintext) should not throw
        var act = () => sut.Mask(ciphertext);
        act.Should().NotThrow();
    }
}
