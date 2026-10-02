using KromicCommerce.Application.Abstractions.Catalog;
using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Application.Behaviors;
using KromicCommerce.Application.Services;

namespace KromicCommerce.Application.DependencyInjection;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(ConcurrencyRetryBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Storefront services — stateless, registered as singletons
        services.AddSingleton<IDeliveryEstimateService, DeliveryEstimateService>();
        services.AddSingleton<IStorefrontStockService, StorefrontStockService>();

        // Commerce services — pure calculation, no I/O, registered as singletons
        services.AddSingleton<IShippingCalculationService, ShippingCalculationService>();
        services.AddSingleton<ITaxCalculationService, TaxCalculationService>();

        // Order workflow services — shared so the customer and admin cancellation paths
        // cannot diverge in their refund-before-cancel semantics.
        services.AddScoped<Features.Orders.OrderCancellationService>();
        // Shared by cancellation and the payment-failure release so both restore stock from the
        // persisted per-order-item lifecycle instead of inferring it from current counters.
        services.AddScoped<Services.OrderInventoryRestorer>();
        // Shared by every review write handler so the denormalised rating aggregate is derived
        // identically on each path that can change the published set.
        services.AddScoped<Features.Catalog.Reviews.ProductReviewRatingRecalculator>();
        services.AddScoped<ICheckoutSummaryService, Services.CheckoutSummaryService>();

        return services;
    }
}
