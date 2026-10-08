using FluentValidation;
using Kiosk.Application.Admin;
using Kiosk.Application.Common;
using Kiosk.Application.Devices;
using Kiosk.Application.Menu;
using Kiosk.Application.Orders;
using Kiosk.Application.Payments;
using Kiosk.Application.Receipts;
using Kiosk.Application.Reports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kiosk.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<OrderingOptions>().Bind(config.GetSection(OrderingOptions.Section)).ValidateOnStart();
        services.AddOptions<ReceiptOptions>().Bind(config.GetSection(ReceiptOptions.Section));
        services.AddOptions<MediaOptions>().Bind(config.GetSection(MediaOptions.Section));
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
        services.AddScoped<MediaService>();
        services.AddScoped<ReceiptService>();
        services.AddScoped<ReportService>();
        return services;
    }
}
