using Kiosk.Api.Auth;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Payments;
using Kiosk.Domain.Orders;
using Kiosk.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kiosk.Api.Controllers.Dev;

/// <summary>
/// Development only, and only while the PayMongo stub is on: completes a pending online payment as if
/// PayMongo's webhook had arrived, through the same <see cref="PaymentWebhookService"/>. Returns 404 anywhere else.
/// </summary>
[ApiController]
[Route("api/dev/orders")]
[Authorize(Policy = Policies.Kiosk)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class DevPaymentsController(
    IWebHostEnvironment env,
    IOptions<PayMongoOptions> payMongo,
    IAppDbContext db,
    PaymentWebhookService webhooks) : ControllerBase
{
    [HttpPost("{id:guid}/simulate-payment")]
    public async Task<IActionResult> SimulatePayment(Guid id, CancellationToken ct)
    {
        if (!env.IsDevelopment() || !payMongo.Value.UseStub)
            return NotFound();

        var deviceId = User.GetDeviceId();
        var payment = await db.Payments
            .Where(p => p.OrderId == id && p.Status == PaymentStatus.Pending && p.ProviderRef != null)
            .Where(p => db.Orders.Any(o => o.Id == id && o.DeviceId == deviceId))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (payment is null)
            return NotFound();

        var outcome = await webhooks.HandleAsync(new PaymentEvent(
            PayMongoOptions.ProviderName, $"evt_dev_{Guid.NewGuid():N}", "dev.simulated_payment",
            Succeeded: true, payment.ProviderRef!, payment.Amount, FailureReason: null), ct);
        return Ok(new { outcome = outcome.ToString() });
    }
}
