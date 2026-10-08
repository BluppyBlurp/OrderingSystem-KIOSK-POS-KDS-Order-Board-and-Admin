using FluentValidation;
using Kiosk.Application.Admin;
using Kiosk.Application.Common;
using Kiosk.Application.Devices;
using Kiosk.Application.Menu;
using Kiosk.Application.Orders;
using Kiosk.Application.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kiosk.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<OrderingOptions>().Bind(config.GetSection(OrderingOptions.Section)).ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<BusinessClock>();
        services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>(includeInternalTypes: true);

        services.AddScoped<MenuQueryService>();
        services.AddScoped<KioskOrderService>();
        services.AddScoped<PosService>();
        services.AddScoped<KdsService>();
        services.AddScoped<BoardService>();
        services.AddScoped<OrderExpiryService>();
        services.AddScoped<PaymentWebhookService>();
        services.AddScoped<AdminMenuService>();
        services.AddScoped<DeviceService>();
        return services;
    }
}
