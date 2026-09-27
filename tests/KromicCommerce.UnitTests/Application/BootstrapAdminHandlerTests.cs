using KromicCommerce.Application.Features.Auth.BootstrapAdmin;
using KromicCommerce.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Tests for BootstrapAdminHandler.
/// Covers: secret validation, one-time guard, credential hashing, username, JWT issuance.
/// </summary>
public sealed class BootstrapAdminHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IPasswordService> _pw = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<IRefreshTokenService> _rts = new();
    private readonly IOptions<AuthTokenOptions> _tokenOpts =
        Microsoft.Extensions.Options.Options.Create(
            new AuthTokenOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 30 });
    private readonly IOptions<BootstrapOptions> _bootstrapOpts =
        Microsoft.Extensions.Options.Options.Create(
            new BootstrapOptions { BootstrapSecret = "correct-bootstrap-secret" });

    private BootstrapAdminHandler CreateHandler() =>
        new(_db.Object, _pw.Object, _jwt.Object, _rts.Object,
            _tokenOpts, _bootstrapOpts,
            NullLogger<BootstrapAdminHandler>.Instance);

    private static BootstrapAdminCommand ValidCommand(string? username = null) =>
        new("admin@example.com", "SecurePass1!", "Alice", "Admin",
            "Kromic Store", "correct-bootstrap-secret", username);

    // -------------------------------------------------------------------------
    // Secret validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_forbidden_when_bootstrap_secret_is_wrong()
    {
        var cmd = ValidCommand() with { BootstrapSecret = "wrong-secret" };
        SetupNoAdminExists();

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BOOTSTRAP_INVALID_SECRET");
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Returns_forbidden_when_bootstrap_secret_is_empty()
    {
        var cmd = ValidCommand() with { BootstrapSecret = "" };
        SetupNoAdminExists();

        var result = await CreateHandler().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BOOTSTRAP_INVALID_SECRET");
    }

    // -------------------------------------------------------------------------
    // One-time guard
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_conflict_when_admin_already_exists()
    {
        SetupAdminAlreadyExists();

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BOOTSTRAP_ALREADY_DONE");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    // -------------------------------------------------------------------------
    // Success path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_token_response_on_successful_bootstrap()
    {
        SetupNoAdminExists();
        SetupSuccessfulPersistence();

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("access_token");
        result.Value.RefreshToken.Should().Be("raw_token");
        result.Value.AccessTokenExpiresInSeconds.Should().Be(15 * 60);
    }

    [Fact]
    public async Task Hashes_password_never_stores_plaintext()
    {
        // The handler must call passwordService.Hash — verifying plaintext is never stored directly
        SetupNoAdminExists();
        SetupSuccessfulPersistence();

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _pw.Verify(p => p.Hash("SecurePass1!"), Times.Once,
            "handler must hash the password, never store plaintext");
    }

    [Fact]
    public async Task Admin_role_is_embedded_in_jwt()
    {
        // JWT is issued with role=Admin — verified via the IssueAccessToken mock call
        SetupNoAdminExists();
        SetupSuccessfulPersistence();

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _jwt.Verify(j => j.IssueAccessToken(
            It.IsAny<Guid>(), "admin@example.com", nameof(UserRole.Admin), It.IsAny<int>()),
            Times.Once,
            "JWT must embed the Admin role");
    }

    [Fact]
    public async Task Sets_username_when_provided()
    {
        // Use a fresh mock DbSet that captures the Add call
        User? capturedUser = null;
        var usersMock = CreateCapturingDbSet<User>(u => capturedUser = u);
        _db.Setup(d => d.Users).Returns(usersMock.Object);

        SetupSuccessfulPersistence(skipUserSetup: true);

        await CreateHandler().Handle(ValidCommand(username: "store_admin"), CancellationToken.None);

        capturedUser.Should().NotBeNull("handler must add a User to the context");
        capturedUser!.Username.Should().Be("store_admin");
        capturedUser.NormalizedUsername.Should().Be("STORE_ADMIN");
    }

    [Fact]
    public async Task Returns_validation_failure_for_too_short_username()
    {
        SetupNoAdminExists();
        SetupSuccessfulPersistence();

        var result = await CreateHandler().Handle(ValidCommand(username: "ab"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("INVALID_USERNAME");
    }

    // -------------------------------------------------------------------------
    // Validator
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("", "password12345", "Alice", "Admin", "Store", "secret")] // no email
    [InlineData("admin@x.com", "short", "Alice", "Admin", "Store", "secret")] // password < 10
    [InlineData("admin@x.com", "password12345", "", "Admin", "Store", "secret")] // no first name
    [InlineData("admin@x.com", "password12345", "Alice", "Admin", "", "secret")] // no business name
    [InlineData("admin@x.com", "password12345", "Alice", "Admin", "Store", "")] // no secret
    public void Validator_rejects_invalid_commands(
        string email, string password, string firstName,
        string lastName, string businessName, string secret)
    {
        var validator = new BootstrapAdminValidator();
        var cmd = new BootstrapAdminCommand(email, password, firstName, lastName, businessName, secret);
        validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_accepts_valid_command()
    {
        var validator = new BootstrapAdminValidator();
        validator.Validate(ValidCommand()).IsValid.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetupNoAdminExists()
    {
        _db.Setup(d => d.Users).Returns(MockDbSet<User>([]));
    }

    private void SetupAdminAlreadyExists()
    {
        var admin = User.CreateAdmin("existing@example.com", "hash", "Existing", "Admin");
        _db.Setup(d => d.Users).Returns(MockDbSet([admin]));
    }

    /// <param name="skipUserSetup">True when the caller has already set up Users separately.</param>
    private void SetupSuccessfulPersistence(bool skipUserSetup = false)
    {
        _pw.Setup(p => p.Hash("SecurePass1!")).Returns("hashed_password");
        _rts.Setup(r => r.GenerateRawToken()).Returns("raw_token");
        _rts.Setup(r => r.HashToken("raw_token")).Returns("hashed_token");
        _jwt.Setup(j => j.IssueAccessToken(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns("access_token");

        if (!skipUserSetup)
            _db.Setup(d => d.Users).Returns(MockDbSet<User>([]));

        _db.Setup(d => d.BusinessSettings.Add(It.IsAny<BusinessSettings>()));
        _db.Setup(d => d.RefreshTokens.Add(It.IsAny<RefreshToken>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    /// <summary>
    /// Creates a mock DbSet that intercepts <c>Add</c> calls to capture the entity.
    /// The returned mock is pre-configured as an empty queryable.
    /// </summary>
    private static Mock<DbSet<T>> CreateCapturingDbSet<T>(Action<T> onAdd) where T : class
    {
        var mock = MockDbSetMock<T>([]);
        mock.Setup(s => s.Add(It.IsAny<T>())).Callback(onAdd);
        return mock;
    }

    private static DbSet<T> MockDbSet<T>(List<T> data) where T : class =>
        MockDbSetMock<T>(data).Object;

    private static Mock<DbSet<T>> MockDbSetMock<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mock = new Mock<DbSet<T>>();
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        return mock;
    }
}
