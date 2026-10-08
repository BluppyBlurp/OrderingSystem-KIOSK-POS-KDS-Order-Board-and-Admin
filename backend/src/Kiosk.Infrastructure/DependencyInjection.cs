using Kiosk.Application.Abstractions;
using Kiosk.Application.Staff;
using Kiosk.Infrastructure.Identity;
using Kiosk.Infrastructure.Jobs;
using Kiosk.Infrastructure.Payments;
using Kiosk.Infrastructure.Persistence;
using Kiosk.Infrastructure.Receipts;
using Kiosk.Infrastructure.Security;
using Kiosk.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kiosk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = PostgresConnectionString.Normalize(
            config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured."));

        // No EnableRetryOnFailure: it rejects the explicit transactions that stock reservation relies on.
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString, npgsql =>
            npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.TryAddScoped<ICurrentActor, NoActor>();
        services.AddScoped<IOrderNumberGenerator, PostgresOrderNumberGenerator>();

        services.AddOptions<SlipOptions>()
            .Bind(config.GetSection(SlipOptions.Section))
            .Validate(o => SlipOptions.IsValidKey(o.SigningKey), "Slip:SigningKey must be a base64 key of at least 32 bytes.")
            .ValidateOnStart(); // fail the deploy, not the first cash order
        services.AddSingleton<ISlipTokenService, HmacSlipTokenService>();

        services.AddOptions<PayMongoOptions>().Bind(config.GetSection(PayMongoOptions.Section));
        services.AddSingleton<PayMongoWebhookParser>();
        if (config.GetValue<bool>($"{PayMongoOptions.Section}:{nameof(PayMongoOptions.UseStub)}"))
        {
            services.AddScoped<IPaymentGateway, StubPaymentGateway>();
        }
        else
        {
            services.AddHttpClient<IPaymentGateway, PayMongoGateway>((sp, http) =>
            {
                http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<PayMongoOptions>>().Value.BaseUrl);
                http.Timeout = TimeSpan.FromSeconds(15);
            });
        }

        services.AddSingleton<IReceiptRenderer, QuestPdfReceiptRenderer>();

        // Staff management (Admin → Staff) calls Clerk's Backend API; without a secret key it answers 503.
        services.AddOptions<ClerkBackendOptions>().Bind(config.GetSection(ClerkBackendOptions.Section));
        services.AddHttpClient<IStaffDirectory, ClerkStaffDirectory>((sp, http) =>
        {
            http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ClerkBackendOptions>>().Value.ApiUrl);
            http.Timeout = TimeSpan.FromSeconds(15);
        });

        // Until Storage:R2 is filled in, the media endpoints answer 503 instead of the API refusing to start.
        services.AddOptions<R2Options>().Bind(config.GetSection(R2Options.Section));
        services.AddSingleton<IMediaStorage, R2MediaStorage>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();

        if (config.GetValue("Jobs:OrderExpiry", true))
            services.AddHostedService<OrderExpiryJob>();

        return services;
    }

    private sealed class NoActor : ICurrentActor
    {
        public string? ActorId => null;
    }
}
