using Kiosk.Application.Orders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kiosk.Infrastructure.Jobs;

/// <summary>Every 30 seconds, expires unpaid orders past their window and releases their stock.</summary>
internal sealed class OrderExpiryJob(IServiceScopeFactory scopes, ILogger<OrderExpiryJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var expired = await scope.ServiceProvider.GetRequiredService<OrderExpiryService>().ExpireDueAsync(stoppingToken);
                if (expired > 0)
                    logger.LogInformation("Expired {Count} unpaid orders", expired);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Order expiry run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
