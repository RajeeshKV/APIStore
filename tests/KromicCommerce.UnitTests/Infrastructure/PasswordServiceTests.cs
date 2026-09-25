using KromicCommerce.Infrastructure.Auth;

namespace KromicCommerce.UnitTests.Infrastructure;

public sealed class PasswordServiceTests
{
    private static readonly PasswordService Sut = new();

    // -----------------------------------------------------------------------
    // Hash / Verify
    // -----------------------------------------------------------------------

    [Fact]
    public void Hash_returns_non_empty_string()
    {
        Sut.Hash("mypassword").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Hash_differs_from_plaintext()
    {
        Sut.Hash("mypassword").Should().NotBe("mypassword");
    }

    [Fact]
    public void Hash_produces_different_output_for_same_input_each_call()
    {
        // PBKDF2 uses a per-call salt
        var a = Sut.Hash("same");
        var b = Sut.Hash("same");
        a.Should().NotBe(b);
    }

    [Fact]
    public void Verify_returns_true_for_correct_password()
    {
        var hash = Sut.Hash("correct-password");
        Sut.Verify("correct-password", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_returns_false_for_wrong_password()
    {
        var hash = Sut.Hash("correct");
        Sut.Verify("wrong", hash).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // GenerateResetToken
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerateResetToken_returns_non_empty_string()
    {
        Sut.GenerateResetToken().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateResetToken_is_url_safe()
    {
        var token = Sut.GenerateResetToken();
        token.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    [Fact]
    public void GenerateResetToken_produces_unique_tokens()
    {
        var a = Sut.GenerateResetToken();
        var b = Sut.GenerateResetToken();
        a.Should().NotBe(b);
    }

    [Fact]
    public void GenerateResetToken_has_sufficient_length()
    {
        // 32 bytes → 43 base64url chars (no padding)
        Sut.GenerateResetToken().Length.Should().BeGreaterThanOrEqualTo(40);
    }

    // -----------------------------------------------------------------------
    // HashToken (SHA-256)
    // -----------------------------------------------------------------------

    [Fact]
    public void HashToken_returns_64_char_hex_string()
    {
        var hash = Sut.HashToken("some-token");
        hash.Length.Should().Be(64);
        hash.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void HashToken_is_deterministic_for_same_input()
    {
        var a = Sut.HashToken("token");
        var b = Sut.HashToken("token");
        a.Should().Be(b);
    }

    [Fact]
    public void HashToken_differs_for_different_inputs()
    {
        Sut.HashToken("token-a").Should().NotBe(Sut.HashToken("token-b"));
    }

    [Fact]
    public void HashToken_and_GenerateResetToken_round_trip()
    {
        var raw = Sut.GenerateResetToken();
        var stored = Sut.HashToken(raw);
        // Simulate verification: re-hash the incoming raw token and compare
        var incoming = Sut.HashToken(raw);
        incoming.Should().Be(stored);
    }

    [Fact]
    public void HashToken_does_not_equal_plaintext()
    {
        var token = "my-reset-token-abc";
        Sut.HashToken(token).Should().NotBe(token);
    }
}
