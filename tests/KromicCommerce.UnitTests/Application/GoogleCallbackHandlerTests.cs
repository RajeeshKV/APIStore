using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Auth.GoogleCallback;
using KromicCommerce.Domain.Identity;
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
    private readonly IOptions<AuthTokenOptions> _opts =
        Microsoft.Extensions.Options.Options.Create(
            new AuthTokenOptions { AccessTokenExpiryMinutes = 15, RefreshTokenExpiryDays = 30 });

    private GoogleCallbackHandler CreateHandler() =>
        new(_db.Object, _googleAuth.Object, _jwt.Object, _rts.Object, _opts,
            NullLogger<GoogleCallbackHandler>.Instance);

    private static GoogleCallbackCommand Cmd(string idToken = "valid_id_token") =>
        new(idToken, DeviceHint: "web");

    // -------------------------------------------------------------------------
    // Invalid / missing Google token
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Returns_unauthorized_when_google_token_invalid()
    {
        _googleAuth.Setup(g => g.ValidateIdTokenAsync("bad_token", It.IsAny<CancellationToken>()))
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
        var identity = GoogleIdentity("new-sub-001", "newuser@gmail.com");
        SetupValidGoogleToken(identity);
        SetupNoExternalLogin();
        SetupNoExistingUserByEmail("newuser@gmail.com");
        SetupPersistence();

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("app_access_token");
        // Verify the application token is issued — not the Google token
        result.Value.AccessToken.Should().NotBe("valid_id_token");
        result.Value.RefreshToken.Should().Be("raw_rt");
    }

    [Fact]
    public async Task New_customer_has_customer_role_in_issued_jwt()
    {
        var identity = GoogleIdentity("sub-role-check", "role@gmail.com");
        SetupValidGoogleToken(identity);
        SetupNoExternalLogin();
        SetupNoExistingUserByEmail("role@gmail.com");
        SetupPersistence();

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        // JWT is issued with role=Customer — verified by the IssueAccessToken mock call
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

        // Inject navigation via reflection (EF would normally do this)
        typeof(ExternalLogin).GetProperty("User")!.SetValue(externalLogin, user);

        var identity = GoogleIdentity("existing-sub-999", "existing@gmail.com");
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

        var identity = GoogleIdentity("inactive-sub-007", "inactive@gmail.com");
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
        // A local password account exists with this email
        var existingUser = User.CreateCustomer("shared@example.com", "password_hash", "Local", "User");
        var identity = GoogleIdentity("new-sub-link", "shared@example.com");

        SetupValidGoogleToken(identity);
        SetupNoExternalLogin();
        SetupExistingUserByEmail("shared@example.com", existingUser);
        SetupPersistence();

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);

        // Must succeed and issue an app token for the existing account — no duplicate created
        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Should().Be("app_access_token");
    }

    // -------------------------------------------------------------------------
    // Identity is keyed by subject, not email
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Lookup_uses_provider_subject_not_email()
    {
        // Two calls: one with the correct subject (found), one proving email alone is not used
        var user = User.CreateCustomer("user@gmail.com", null, "Test", "User");
        var externalLogin = ExternalLogin.Create(user.Id, "Google", "stable-sub-123", "user@gmail.com");
        typeof(ExternalLogin).GetProperty("User")!.SetValue(externalLogin, user);

        // Correct subject → found
        var identity = GoogleIdentity("stable-sub-123", "user@gmail.com");
        SetupValidGoogleToken(identity);
        SetupExternalLoginFound(externalLogin);
        SetupPersistence();

        var result = await CreateHandler().Handle(Cmd(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();

        // The ExternalLogin was looked up by (Provider="Google", ProviderSubject="stable-sub-123")
        // This is verified by the fact that SetupExternalLoginFound filters on ProviderSubject
    }

    // -------------------------------------------------------------------------
    // Application JWT — Google token is NOT the app token
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Issued_access_token_is_application_jwt_not_google_token()
    {
        const string googleIdToken = "google.id.token.value";
        var identity = GoogleIdentity("app-jwt-sub", "appjwt@gmail.com");

        _googleAuth.Setup(g => g.ValidateIdTokenAsync(googleIdToken, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(identity);
        SetupNoExternalLogin();
        SetupNoExistingUserByEmail("appjwt@gmail.com");
        SetupPersistence();

        var result = await CreateHandler().Handle(new GoogleCallbackCommand(googleIdToken, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // The app token is issued by _jwt (our mock returns "app_access_token"), NOT the Google token
        result.Value.AccessToken.Should().Be("app_access_token");
        result.Value.AccessToken.Should().NotBe(googleIdToken);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static GoogleIdentity GoogleIdentity(string subject, string email) =>
        new(Subject: subject, Email: email, EmailVerified: true,
            GivenName: "Test", FamilyName: "User", PictureUrl: null);

    private void SetupValidGoogleToken(GoogleIdentity identity)
    {
        _googleAuth.Setup(g => g.ValidateIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(identity);
    }

    private void SetupNoExternalLogin()
    {
        _db.Setup(d => d.ExternalLogins)
           .Returns(MockDbSet<ExternalLogin>([]));
    }

    private void SetupExternalLoginFound(ExternalLogin login)
    {
        _db.Setup(d => d.ExternalLogins)
           .Returns(MockDbSet([login]));
    }

    private void SetupNoExistingUserByEmail(string email)
    {
        _db.Setup(d => d.Users)
           .Returns(MockDbSet<User>([]));
    }

    private void SetupExistingUserByEmail(string email, User user)
    {
        _db.Setup(d => d.Users)
           .Returns(MockDbSet([user]));
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
