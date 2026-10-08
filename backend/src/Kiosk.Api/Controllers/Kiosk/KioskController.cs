using Kiosk.Api.Auth;
using Kiosk.Application.Menu;
using Kiosk.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kiosk.Api.Controllers.Kiosk;

/// <summary>Customer kiosk. Device token only; a kiosk can read the menu and its own orders, nothing else.</summary>
[ApiController]
[Route("api/kiosk")]
[Authorize(Policy = Policies.Kiosk)]
public sealed class KioskController(MenuQueryService menu, KioskOrderService orders) : ControllerBase
{
    [HttpGet("menu")]
    public Task<MenuDto> GetMenu(CancellationToken ct) => menu.GetKioskMenuAsync(ct);

    /// <summary>Creates the order from server-side prices and reserves stock. Client prices are never accepted.</summary>
    [HttpPost("orders")]
    [EnableRateLimiting(RateLimits.KioskOrders)]
    public async Task<ActionResult<KioskOrderDto>> CreateOrder(CreateOrderRequest request, CancellationToken ct)
    {
        var result = await orders.CreateAsync(request, User.GetDeviceId(), ct);
        return CreatedAtAction(nameof(GetOrder), new { id = result.Order.Id }, result);
    }

    /// <summary>Cash → slip with QR token. E-wallet/card → checkout URL.</summary>
    [HttpPost("orders/{id:guid}/pay")]
    [EnableRateLimiting(RateLimits.KioskOrders)]
    public Task<KioskOrderDto> Pay(Guid id, PayRequest request, CancellationToken ct) =>
        orders.PayAsync(id, request, User.GetDeviceId(), ct);

    [HttpPost("orders/{id:guid}/cancel-checkout")]
    public Task<KioskOrderDto> CancelCheckout(Guid id, CancellationToken ct) =>
        orders.CancelCheckoutAsync(id, User.GetDeviceId(), ct);

    /// <summary>Polling fallback for when the realtime push is late.</summary>
    [HttpGet("orders/{id:guid}")]
    public Task<KioskOrderDto> GetOrder(Guid id, CancellationToken ct) =>
        orders.GetAsync(id, User.GetDeviceId(), ct);

    /// <summary>For the soft "Table 12 is already in use — continue?" warning.</summary>
    [HttpGet("tables/{tableNumber:int}")]
    public Task<TableStatusDto> GetTable(int tableNumber, CancellationToken ct) =>
        orders.GetTableStatusAsync(tableNumber, ct);
}
