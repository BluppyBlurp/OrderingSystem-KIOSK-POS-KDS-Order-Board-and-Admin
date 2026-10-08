using Kiosk.Application.Payments;
using Kiosk.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kiosk.Api.Controllers.Webhooks;

/// <summary>
/// The only anonymous business endpoint. Trust comes from the HMAC signature, not from the caller.
/// Always 200 for a correctly signed event (even duplicates), so PayMongo stops retrying.
/// </summary>
[ApiController]
[Route("api/webhooks/paymongo")]
[AllowAnonymous]
[EnableRateLimiting(RateLimits.Webhooks)]
public sealed class PayMongoWebhookController(
    PayMongoWebhookParser parser,
    PaymentWebhookService webhooks,
    ILogger<PayMongoWebhookController> logger) : ControllerBase
{
    private const int MaxBodyBytes = 256 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // The signature covers the exact bytes, so read the raw body rather than model-binding it.
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        var evt = parser.Parse(body, Request.Headers[PayMongoWebhookParser.SignatureHeader]);
        if (evt is null)
            return Ok(new { outcome = "ignored" });

        var outcome = await webhooks.HandleAsync(evt, ct);
        logger.LogInformation("PayMongo event {EventId} ({Type}): {Outcome}", evt.EventId, evt.EventType, outcome);
        return Ok(new { outcome = outcome.ToString() });
    }
}
