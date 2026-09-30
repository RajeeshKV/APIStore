using KromicCommerce.Application.Abstractions.Payments;
using KromicCommerce.Application.Features.Checkout;
using Microsoft.Extensions.Logging.Abstractions;

namespace KromicCommerce.UnitTests.Application;

public sealed class CheckoutAddressAuthorizationTests
{
    [Fact]
    public async Task Checkout_rejects_an_address_owned_by_another_customer()
    {
        var ownerId = Guid.NewGuid();
        var requestingCustomerId = Guid.NewGuid();
        var address = CustomerAddress.Create(
            ownerId, "Home", "Jane", "Doe", null,
            "123 Street", null, "Mumbai", "Maharashtra", "400001", "IN", "+919876543210");

        var db = new Mock<IApplicationDbContext>();
        db.Setup(d => d.Orders).Returns(MockDbSet<Order>([]));
        db.Setup(d => d.CustomerAddresses).Returns(MockDbSet([address]));

        var handler = new CheckoutHandler(
            db.Object,
            Mock.Of<IBusinessSettingsService>(),
            Mock.Of<IShippingCalculationService>(),
            Mock.Of<ITaxCalculationService>(),
            Mock.Of<IPromotionService>(),
            Mock.Of<IPaymentGateway>(),
            NullLogger<CheckoutHandler>.Instance);

        var result = await handler.Handle(
            new CheckoutCommand(
                requestingCustomerId, address.Id, PaymentMethod.Razorpay, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ADDRESS_NOT_FOUND");
        result.Error.Type.Should().Be(ErrorType.NotFound);
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
