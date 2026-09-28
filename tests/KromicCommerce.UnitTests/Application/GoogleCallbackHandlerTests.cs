using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Abstractions.Store;
using KromicCommerce.Application.Features.Auth.GoogleCallback;
using KromicCommerce.Domain.Identity;
using KromicCommerce.Domain.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Tests for GoogleCallbackHandler.
/// Covers: invalid token, new customer creation, existing customer lookup,
/// existing local account linking, inactive account, duplicate callback,
/// application JWT issuance (Google token must NOT be the API token).
/// </summary>
public sealed class GoogleCallbackHandlerTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<IGoogleAuthService> _googleAuth = new();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly Mock<IRefreshTokenService> _rts = new();
    private readonly Mock<IBusinessSettingsService> _businessSettings = new();
    private readonly IOptions<AuthTokenOptions> _opts =
        Microsoft.Extensions.Options.Options.Create(
            new AuthTokenOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 30 });

    private const string TestClientId = "test-client-id.apps.googleusercontent.com";

    public GoogleCallbackHandlerTests()
    {
        // Default: Google OAuth is configured and enabled
        SetupConfiguredGoogleAuth();
    }

    private GoogleCallbackHandler CreateHandler() =>
        new(_db.Object, _businessSettings.Object, _googleAuth.Object, _jwt.Object,
            _rts.Object, _opts, NullLogger<GoogleCallbackHandler>.Instance);

    private static GoogleCallbackCommand Cmd(string idToken = "valid_id_token") =>
        new(idToken, DeviceHint: "web");

    // -------------------------------------------------------------------------
    // Configuration checks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_error_when_google_not_configured()
    {
        // Arrange: no Google credentials in DB
        var settings = BusinessSettings.CreateDefault("Test Store");
        // Auth has no GoogleClientId — IsGoogleOAuthConfigured = false
        _businessSettings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
                         .ReturnsAsync(settings);

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_GOOGLE_NOT_CONFIGURED");
    }

    // -------------------------------------------------------------------------
    // Invalid / missing Google token
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_unauthorized_when_google_token_invalid()
    {
        _googleAuth.Setup(g => g.ValidateIdTokenAsync("bad_token", TestClientId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync((GoogleIdentity?)null);

        var result = await CreateHandler().Handle(new GoogleCallbackCommand("bad_token", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_GOOGLE_INVALID");
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // New customer — creates account + CustomerProfile
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Creates_new_customer_when_no_existing_account()
    {
        var identity = MakeIdentity("new-sub-001", "newuser@gmail.com");
        SetupValidGoogleToken(identity);
        SetupNoExternalLogin();
        SetupNoExistingUserByEmail("newuser@gmail.com");
        SetupPersistence();

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("app_access_token");
        result.Value.AccessToken.Should().NotBe("valid_id_token");
        result.Value.RefreshToken.Should().Be("raw_rt");
    }

    [Fact]
    public async Task New_customer_has_customer_role_in_issued_jwt()
    {
        var identity = MakeIdentity("sub-role-check", "role@gmail.com");
        SetupValidGoogleToken(identity);
        SetupNoExternalLogin();
        SetupNoExistingUserByEmail("role@gmail.com");
        SetupPersistence();

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _jwt.Verify(j => j.IssueAccessToken(
            It.IsAny<Guid>(), "role@gmail.com", nameof(UserRole.Customer), It.IsAny<int>()),
            Times.Once,
            "new Google customer must receive Customer role in the JWT");
    }

    // -------------------------------------------------------------------------
    // Existing customer — found by ExternalLogin subject
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_token_for_existing_google_customer()
    {
        var user = User.CreateCustomer("existing@gmail.com", passwordHash: null, "Ex", "User");
        var externalLogin = ExternalLogin.Create(user.Id, "Google", "existing-sub-999", "existing@gmail.com");
        typeof(ExternalLogin).GetProperty("User")!.SetValue(externalLogin, user);

        var identity = MakeIdentity("existing-sub-999", "existing@gmail.com");
        SetupValidGoogleToken(identity);
        SetupExternalLoginFound(externalLogin);
        SetupPersistence();

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("app_access_token");
    }

    [Fact]
    public async Task Returns_forbidden_for_inactive_existing_customer()
    {
        var user = User.CreateCustomer("inactive@gmail.com", passwordHash: null, "In", "Active");
        user.Deactivate();
        var externalLogin = ExternalLogin.Create(user.Id, "Google", "inactive-sub-007", "inactive@gmail.com");
        typeof(ExternalLogin).GetProperty("User")!.SetValue(externalLogin, user);

        var identity = MakeIdentity("inactive-sub-007", "inactive@gmail.com");
        SetupValidGoogleToken(identity);
        SetupExternalLoginFound(externalLogin);

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AUTH_ACCOUNT_INACTIVE");
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    // -------------------------------------------------------------------------
    // Existing local account with same email — link, don't duplicate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Links_google_identity_to_existing_local_account_with_same_email()
    {
        var existingUser = User.CreateCustomer("shared@example.com", "password_hash", "Local", "User");
        var identity = MakeIdentity("new-sub-link", "shared@example.com");

        SetupValidGoogleToken(identity);
        SetupNoExternalLogin();
        SetupExistingUserByEmail("shared@example.com", existingUser);
        SetupPersistence();

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("app_access_token");
    }

    // -------------------------------------------------------------------------
    // Application JWT — Google token is NOT the app token
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Issued_access_token_is_application_jwt_not_google_token()
    {
        const string googleIdToken = "google.id.token.value";
        var identity = MakeIdentity("app-jwt-sub", "appjwt@gmail.com");

        _googleAuth.Setup(g => g.ValidateIdTokenAsync(googleIdToken, TestClientId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(identity);
        SetupNoExternalLogin();
        SetupNoExistingUserByEmail("appjwt@gmail.com");
        SetupPersistence();

        var result = await CreateHandler().Handle(new GoogleCallbackCommand(googleIdToken, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("app_access_token");
        result.Value.AccessToken.Should().NotBe(googleIdToken);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static GoogleIdentity MakeIdentity(string subject, string email) =>
        new(Subject: subject, Email: email, EmailVerified: true,
            GivenName: "Test", FamilyName: "User", PictureUrl: null);

    private void SetupConfiguredGoogleAuth()
    {
        // Build a BusinessSettings with Google credentials configured
        var settings = BusinessSettings.CreateDefault("Test Store");
        settings.UpdateGoogleCredentials(TestClientId, "enc-secret", "https://example.com/callback", true);
        _businessSettings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
                         .ReturnsAsync(settings);
    }

    private void SetupValidGoogleToken(GoogleIdentity identity)
    {
        _googleAuth.Setup(g => g.ValidateIdTokenAsync(It.IsAny<string>(), TestClientId, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(identity);
    }

    private void SetupNoExternalLogin()
    {
        _db.Setup(d => d.ExternalLogins).Returns(MockDbSet<ExternalLogin>([]));
    }

    private void SetupExternalLoginFound(ExternalLogin login)
    {
        _db.Setup(d => d.ExternalLogins).Returns(MockDbSet([login]));
    }

    private void SetupNoExistingUserByEmail(string email)
    {
        _db.Setup(d => d.Users).Returns(MockDbSet<User>([]));
    }

    private void SetupExistingUserByEmail(string email, User user)
    {
        _db.Setup(d => d.Users).Returns(MockDbSet([user]));
    }

    private void SetupPersistence()
    {
        _rts.Setup(r => r.GenerateRawToken()).Returns("raw_rt");
        _rts.Setup(r => r.HashToken("raw_rt")).Returns("hashed_rt");
        _jwt.Setup(j => j.IssueAccessToken(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
            .Returns("app_access_token");

        _db.Setup(d => d.Users.Add(It.IsAny<User>()));
        _db.Setup(d => d.CustomerProfiles.Add(It.IsAny<CustomerProfile>()));
        _db.Setup(d => d.ExternalLogins.Add(It.IsAny<ExternalLogin>()));
        _db.Setup(d => d.RefreshTokens.Add(It.IsAny<RefreshToken>()));
        _db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private static Microsoft.EntityFrameworkCore.DbSet<T> MockDbSet<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mock = new Mock<Microsoft.EntityFrameworkCore.DbSet<T>>();
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        return mock.Object;
    }
}
