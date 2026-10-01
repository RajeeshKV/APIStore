using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Features.Checkout;
using KromicCommerce.Application.Services;

namespace KromicCommerce.UnitTests.Application;

public sealed class CheckoutPhoneVerificationTests
{
    private static User Customer(string? phone, bool verified)
    {
        var user = User.CreateCustomer("jane@example.com", null, "Jane", "Doe");
        if (phone is not null)
            user.SetPhoneNumber(phone, verified);
        return user;
    }

    private static CustomerAddress AddressWithPhone(User owner, string? phone) => CustomerAddress.Create(
        owner.Id, "Home", "Jane", "Doe", null,
        "123 Street", null, "Mumbai", "Maharashtra", "400001", "IN", phone);

    /// <summary>
    /// Runs checkout up to the verification gate. Pricing is stubbed to fail with a sentinel
    /// error, so "the error is not a verification error" proves the gate was passed.
    /// </summary>
    private static Task<Result<CheckoutResponse>> RunAsync(
        User user, CustomerAddress address, bool requireVerification, bool smsConfigured)
    {
        var db = new Mock<IApplicationDbContext>();
        db.Setup(d => d.Orders).Returns(MockDbSet<Order>([]));
        db.Setup(d => d.CustomerAddresses).Returns(MockDbSet<CustomerAddress>([address]));
        db.Setup(d => d.Users).Returns(MockDbSet<User>([user]));

        var summary = new Mock<ICheckoutSummaryService>();
        summary.Setup(s => s.CalculateAsync(It.IsAny<CheckoutSummaryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<CheckoutSummary>(
                Error.Validation("REACHED_PRICING", "Sentinel: pricing was reached.")));

        var handler = new CheckoutHandler(
            db.Object,
            Mock.Of<IBusinessSettingsService>(),
            summary.Object,
            Mock.Of<IPaymentGateway>(),
            new OrderInventoryRestorer(
                db.Object,
                Mock.Of<ICatalogCacheService>(),
                NullLogger<OrderInventoryRestorer>.Instance),
            smsConfigured ? SmsTestDoubles.Configured() : SmsTestDoubles.NotConfigured(),
            SmsTestDoubles.Policy(requireVerifiedPhoneAtCheckout: requireVerification),
            NullLogger<CheckoutHandler>.Instance);

        return handler.Handle(
            new CheckoutCommand(user.Id, address.Id, PaymentMethod.Razorpay, null, null),
            CancellationToken.None);
    }

    private static void AssertPassedTheGate(Result<CheckoutResponse> result)
        => result.Error.Code.Should().Be("REACHED_PRICING");

    [Fact]
    public async Task Rejects_an_unverified_phone_when_verification_is_required()
    {
        var user = Customer("+919876543210", verified: false);
        var address = AddressWithPhone(user, "+919876543210");

        var result = await RunAsync(user, address, requireVerification: true, smsConfigured: true);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PHONE_VERIFICATION_REQUIRED");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task Rejects_a_delivery_number_that_is_not_the_verified_one()
    {
        var user = Customer("+919876543210", verified: true);
        var address = AddressWithPhone(user, "+919999999999");

        var result = await RunAsync(user, address, requireVerification: true, smsConfigured: true);

        result.Error.Code.Should().Be("ADDRESS_PHONE_MISMATCH");
    }

    [Fact]
    public async Task Accepts_a_matching_address_phone_in_a_different_format()
    {
        var user = Customer("+919876543210", verified: true);
        var address = AddressWithPhone(user, "9876543210");

        var result = await RunAsync(user, address, requireVerification: true, smsConfigured: true);

        AssertPassedTheGate(result);
    }

    [Fact]
    public async Task Does_not_require_verification_when_sms_is_not_configured()
    {
        var user = Customer("+919876543210", verified: false);
        var address = AddressWithPhone(user, "+919999999999");

        // Policy says "require it", but no provider exists to deliver a code. Blocking here
        // would make the store unsellable for every customer.
        var result = await RunAsync(user, address, requireVerification: true, smsConfigured: false);

        AssertPassedTheGate(result);
    }

    [Fact]
    public async Task Does_not_require_verification_when_the_policy_does_not_ask_for_it()
    {
        var user = Customer("+919876543210", verified: false);
        var address = AddressWithPhone(user, "+919999999999");

        var result = await RunAsync(user, address, requireVerification: false, smsConfigured: true);

        AssertPassedTheGate(result);
    }

    [Fact]
    public async Task A_customer_with_no_number_at_all_cannot_pass_the_gate()
    {
        var user = Customer(phone: null, verified: false);
        var address = AddressWithPhone(user, "+919876543210");

        var result = await RunAsync(user, address, requireVerification: true, smsConfigured: true);

        result.Error.Code.Should().Be("PHONE_VERIFICATION_REQUIRED");
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
