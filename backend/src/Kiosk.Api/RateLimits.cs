using System.Threading.RateLimiting;
using Kiosk.Api.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace Kiosk.Api;

/// <summary>Order creation and webhooks are the two routes most worth limiting (docs §4 rule 7).</summary>
public static class RateLimits
{
    public const string KioskOrders = "kiosk-orders";
    public const string Webhooks = "webhooks";

    public static IServiceCollection AddKioskRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Per device: a real customer needs a few calls per order; this stops a runaway or tampered kiosk.
            o.AddPolicy(KioskOrders, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst(Claims.DeviceId)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));

            o.AddPolicy(Webhooks, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1) }));
        });
}
