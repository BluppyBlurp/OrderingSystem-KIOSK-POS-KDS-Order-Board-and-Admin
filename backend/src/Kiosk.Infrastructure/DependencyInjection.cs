using Kiosk.Application.Abstractions;
using Kiosk.Infrastructure.Jobs;
using Kiosk.Infrastructure.Payments;
using Kiosk.Infrastructure.Persistence;
using Kiosk.Infrastructure.Security;
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

        if (config.GetValue("Jobs:OrderExpiry", true))
            services.AddHostedService<OrderExpiryJob>();

        return services;
    }

    private sealed class NoActor : ICurrentActor
    {
        public string? ActorId => null;
    }
}
