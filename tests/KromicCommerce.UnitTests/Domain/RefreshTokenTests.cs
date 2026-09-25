using KromicCommerce.Domain.Identity;
using RefreshToken = KromicCommerce.Domain.Identity.RefreshToken;

namespace KromicCommerce.UnitTests.Domain;

public sealed class RefreshTokenTests
{
    [Fact]
    public void Active_token_is_not_revoked_and_not_expired()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(30));

        token.IsActive.Should().BeTrue();
        token.IsRevoked.Should().BeFalse();
        token.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void Revoked_token_is_not_active()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(30));
        token.Revoke();
        token.IsRevoked.Should().BeTrue();
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Expired_token_is_not_active()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(), "hash", DateTime.UtcNow.AddSeconds(-1));
        token.IsExpired.Should().BeTrue();
        token.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Revoke_records_replacement_token_id()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(), "hash", DateTime.UtcNow.AddDays(30));
        var newId = Guid.NewGuid();
        token.Revoke(newId);
        token.ReplacedByTokenId.Should().Be(newId);
    }
}
