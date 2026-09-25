using KromicCommerce.Application.Features.Auth.RefreshToken;
using KromicCommerce.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RefreshToken = KromicCommerce.Domain.Identity.RefreshToken;

namespace KromicCommerce.UnitTests.Application;

public sealed class RefreshTokenHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<IRefreshTokenService> _rts = new();
    private readonly IOptions<AuthTokenOptions> _opts =
        Microsoft.Extensions.Options.Options.Create(new AuthTokenOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 30 });

    private RefreshTokenHandler CreateHandler() =>
        new(_db.Object, _jwt.Object, _rts.Object, _opts,
            NullLogger<RefreshTokenHandler>.Instance);

    [Fact]
    public async Task Returns_failure_when_token_not_found()
    {
        _rts.Setup(r => r.HashToken("bad")).Returns("badhash");
        SetupTokens(new List<RefreshToken>());

        var result = await CreateHandler().Handle(
            new RefreshTokenCommand("bad", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Returns_failure_and_revokes_all_on_reuse_of_revoked_token()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        var revokedToken = RefreshToken.Create(user.Id, "revoked_hash", DateTime.UtcNow.AddDays(30));
        revokedToken.Revoke();

        _rts.Setup(r => r.HashToken("old_raw")).Returns("revoked_hash");
        var allTokens = new List<RefreshToken> { revokedToken };
        SetupTokensWithUser(allTokens, user, revokedToken);

        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(
            new RefreshTokenCommand("old_raw", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_INVALID_REFRESH_TOKEN");
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void SetupTokens(List<RefreshToken> tokens)
    {
        var data = tokens.AsQueryable();
        var mock = new Mock<DbSet<RefreshToken>>();
        mock.As<IAsyncEnumerable<RefreshToken>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<RefreshToken>(data.GetEnumerator()));
        mock.As<IQueryable<RefreshToken>>()
            .Setup(m => m.Provider).Returns(new TestAsyncQueryProvider<RefreshToken>(data.Provider));
        mock.As<IQueryable<RefreshToken>>().Setup(m => m.Expression).Returns(data.Expression);
        mock.As<IQueryable<RefreshToken>>().Setup(m => m.ElementType).Returns(data.ElementType);
        mock.As<IQueryable<RefreshToken>>().Setup(m => m.GetEnumerator()).Returns(data.GetEnumerator());
        _db.Setup(d => d.RefreshTokens).Returns(mock.Object);
    }

    private void SetupTokensWithUser(
        List<RefreshToken> tokens,
        User user,
        RefreshToken target)
    {
        // Use reflection to set navigation property since EF sets it
        typeof(RefreshToken)
            .GetProperty("User")!
            .SetValue(target, user);

        SetupTokens(tokens);

        var userList = new List<User> { user }.AsQueryable();
        var userMock = new Mock<DbSet<User>>();
        userMock.As<IAsyncEnumerable<User>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<User>(userList.GetEnumerator()));
        userMock.As<IQueryable<User>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<User>(userList.Provider));
        userMock.As<IQueryable<User>>().Setup(m => m.Expression).Returns(userList.Expression);
        userMock.As<IQueryable<User>>().Setup(m => m.ElementType).Returns(userList.ElementType);
        userMock.As<IQueryable<User>>().Setup(m => m.GetEnumerator()).Returns(userList.GetEnumerator());
        _db.Setup(d => d.Users).Returns(userMock.Object);
    }
}
