using System.Security.Cryptography;
using KromicCommerce.Infrastructure.Configuration;
using KromicCommerce.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace KromicCommerce.UnitTests.Infrastructure;

public sealed class EnvironmentKeySecretProtectionServiceTests
{
    private static EnvironmentKeySecretProtectionService CreateSut() =>
        new(Options.Create(new SecretProtectionOptions
        {
            EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }));

    [Fact]
    public void Protect_and_unprotect_round_trip_with_the_environment_key()
    {
        var sut = CreateSut();

        var ciphertext = sut.Protect("rzp_secret_value");

        ciphertext.Should().StartWith("v1:");
        ciphertext.Should().NotContain("rzp_secret_value");
        sut.Unprotect(ciphertext).Should().Be("rzp_secret_value");
    }

    [Fact]
    public void Unprotect_rejects_a_ciphertext_from_a_different_key()
    {
        var ciphertext = CreateSut().Protect("secret");

        var act = () => CreateSut().Unprotect(ciphertext);

        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("c2hvcnQ=")]
    public void Constructor_rejects_an_invalid_environment_key(string key)
    {
        var act = () => new EnvironmentKeySecretProtectionService(
            Options.Create(new SecretProtectionOptions { EncryptionKey = key }));

        act.Should().Throw<InvalidOperationException>();
    }
}
