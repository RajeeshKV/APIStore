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
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Storefront services — stateless, registered as singletons
        services.AddSingleton<IDeliveryEstimateService, DeliveryEstimateService>();
        services.AddSingleton<IStorefrontStockService, StorefrontStockService>();

        // Commerce services — pure calculation, no I/O, registered as singletons
        services.AddSingleton<IShippingCalculationService, ShippingCalculationService>();
        services.AddSingleton<ITaxCalculationService, TaxCalculationService>();

        return services;
    }
}
