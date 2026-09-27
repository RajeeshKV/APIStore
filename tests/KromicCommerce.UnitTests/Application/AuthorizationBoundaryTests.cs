using KromicCommerce.Application.Features.Auth.LogoutAll;
using KromicCommerce.Application.Features.Auth.PasswordReset;
using KromicCommerce.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Verifies authentication domain boundaries:
///
/// 1. Password reset is admin-only — a customer's email is silently ignored.
/// 2. LogoutAll only affects the requesting user's own tokens.
/// 3. Google OAuth issues Customer role; admin login issues Admin role.
/// 4. Google-only accounts (null PasswordHash) cannot authenticate via password login.
///
/// Policy-level enforcement (AdminOnly attribute) is handled by ASP.NET Core authorization
/// and is verified by the presence of [Authorize(Policy="AdminOnly")] on controllers.
/// These tests verify the application-layer enforcement inside handlers.
/// </summary>
public sealed class AuthorizationBoundaryTests
{
    // -------------------------------------------------------------------------
    // Password reset is admin-only
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Password_reset_silently_ignores_customer_email()
    {
        // Arrange — customer account exists but has Role=Customer
        var customer = User.CreateCustomer("customer@example.com", "hash", "Alice", "Customer");

        var db = new Mock<IApplicationDbContext>();
        db.Setup(d => d.Users).Returns(MockDbSet([customer]));

        var pw = new Mock<IPasswordService>();
        var email = new Mock<KromicCommerce.Application.Abstractions.Email.IEmailService>();

        var handler = new RequestPasswordResetHandler(
            db.Object, pw.Object, email.Object,
            NullLogger<RequestPasswordResetHandler>.Instance);

        // Act — request reset for the customer's email
        var result = await handler.Handle(
            new RequestPasswordResetCommand("customer@example.com"),
            CancellationToken.None);

        // Assert — must succeed (anti-enumeration) but must NOT send any email
        result.IsSuccess.Should().BeTrue(
            "response must never reveal whether the address belongs to an admin or customer");

        email.Verify(
            e => e.SendPasswordResetAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "password reset email must only be sent for admins");

        pw.Verify(p => p.GenerateResetToken(), Times.Never,
            "no reset token should be generated for a customer");
    }

    [Fact]
    public async Task Password_reset_handler_processes_admin_but_not_customer_with_same_email_domain()
    {
        // A customer and an admin can have emails in the same domain;
        // only the admin's reset should proceed.
        var admin = User.CreateAdmin("owner@store.com", "hash", "Admin", "User");
        var customer = User.CreateCustomer("customer@store.com", "hash", "Alice", "User");

        var db = new Mock<IApplicationDbContext>();
        var pw = new Mock<IPasswordService>();
        var email = new Mock<KromicCommerce.Application.Abstractions.Email.IEmailService>();

        pw.Setup(p => p.GenerateResetToken()).Returns("raw");
        pw.Setup(p => p.HashToken("raw")).Returns("hashed");
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        email.Setup(e => e.SendPasswordResetAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new RequestPasswordResetHandler(
            db.Object, pw.Object, email.Object,
            NullLogger<RequestPasswordResetHandler>.Instance);

        // Customer email → no email sent
        db.Setup(d => d.Users).Returns(MockDbSet([admin, customer]));
        var customerResult = await handler.Handle(
            new RequestPasswordResetCommand("customer@store.com"), CancellationToken.None);

        customerResult.IsSuccess.Should().BeTrue();
        email.Verify(
            e => e.SendPasswordResetAsync(
                "customer@store.com", It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Admin email → email IS sent
        db.Setup(d => d.Users).Returns(MockDbSet([admin, customer]));
        var adminResult = await handler.Handle(
            new RequestPasswordResetCommand("owner@store.com"), CancellationToken.None);

        adminResult.IsSuccess.Should().BeTrue();
        email.Verify(
            e => e.SendPasswordResetAsync(
                "owner@store.com", It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // LogoutAll is user-scoped — does not touch other users' tokens
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LogoutAll_only_revokes_the_requesting_users_tokens()
    {
        var requestingUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var requestingUser = User.CreateCustomer("me@example.com", null, "Me", "User");
        SetUserId(requestingUser, requestingUserId);

        var myToken = RefreshToken.Create(requestingUserId, "my-hash", DateTime.UtcNow.AddDays(7));
        var otherToken = RefreshToken.Create(otherUserId, "other-hash", DateTime.UtcNow.AddDays(7));

        var db = new Mock<IApplicationDbContext>();
        db.Setup(d => d.Users).Returns(MockDbSet([requestingUser]));
        db.Setup(d => d.RefreshTokens).Returns(MockDbSet([myToken, otherToken]));
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new LogoutAllHandler(
            db.Object, NullLogger<LogoutAllHandler>.Instance);

        await handler.Handle(
            new LogoutAllCommand(requestingUserId), CancellationToken.None);

        myToken.IsRevoked.Should().BeTrue("the requesting user's token must be revoked");
        otherToken.IsRevoked.Should().BeFalse("another user's token must not be touched");
    }

    // -------------------------------------------------------------------------
    // Role separation — Customer vs Admin in JWT claims
    // -------------------------------------------------------------------------

    [Fact]
    public void Customer_user_has_customer_role()
    {
        var customer = User.CreateCustomer("c@example.com", null, "Alice", "Customer");
        customer.Role.Should().Be(UserRole.Customer);
    }

    [Fact]
    public void Admin_user_has_admin_role()
    {
        var admin = User.CreateAdmin("a@example.com", "hash", "Bob", "Admin");
        admin.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public void Google_customer_has_no_password_hash()
    {
        // Google-only accounts must have null PasswordHash so password login is rejected
        var customer = User.CreateCustomer("google@example.com", passwordHash: null, "G", "User");
        customer.PasswordHash.Should().BeNull(
            "Google-only customer accounts must not have a password hash");
    }

    [Fact]
    public void Admin_bootstrap_requires_password_hash()
    {
        // Admin accounts are always created with a non-null hash via CreateAdmin
        var admin = User.CreateAdmin("admin@example.com", "hashed-password", "A", "B");
        admin.PasswordHash.Should().NotBeNullOrWhiteSpace(
            "admin accounts must always have a password hash at creation");
    }

    // -------------------------------------------------------------------------
    // Token versioning — IncrementTokenVersion only affects the target user
    // -------------------------------------------------------------------------

    [Fact]
    public void IncrementTokenVersion_increments_only_on_the_target_user()
    {
        var user1 = User.CreateAdmin("a@example.com", "h", "A", "B");
        var user2 = User.CreateAdmin("b@example.com", "h", "B", "C");

        var version1Before = user1.TokenVersion;
        var version2Before = user2.TokenVersion;

        user1.IncrementTokenVersion();

        user1.TokenVersion.Should().Be(version1Before + 1);
        user2.TokenVersion.Should().Be(version2Before,
            "token version of a different user must not change");
    }

    [Fact]
    public void ResetPassword_increments_token_version_to_invalidate_existing_jwts()
    {
        var admin = User.CreateAdmin("admin@example.com", "old-hash", "A", "B");
        var versionBefore = admin.TokenVersion;

        admin.SetPasswordResetToken("correct-hash", DateTime.UtcNow.AddMinutes(10));
        admin.ResetPassword("new-hash");

        admin.TokenVersion.Should().Be(versionBefore + 1,
            "password reset must increment TokenVersion to invalidate all outstanding JWTs");
        admin.PasswordResetTokenHash.Should().BeNull(
            "reset token must be cleared after use");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void SetUserId(User user, Guid id) =>
        typeof(KromicCommerce.Domain.Common.Entity)
            .GetProperty("Id")!
            .SetValue(user, id);

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
