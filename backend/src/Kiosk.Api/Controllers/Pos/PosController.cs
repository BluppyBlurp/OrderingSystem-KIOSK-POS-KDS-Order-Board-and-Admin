using Kiosk.Api.Auth;
using Kiosk.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Api.Controllers.Pos;

/// <summary>Cashier POS: find the order from the slip, take cash, never retype the order.</summary>
[ApiController]
[Route("api/pos/orders")]
[Authorize(Policy = Policies.Pos)]
public sealed class PosController(PosService pos) : ControllerBase
{
    /// <summary>Live queue of orders waiting for cash at the counter.</summary>
    [HttpGet]
    public Task<IReadOnlyList<OrderDto>> GetPendingCash(CancellationToken ct) => pos.GetPendingCashAsync(ct);

    /// <summary>Scanned QR token, or a typed order number such as "A-101" or "101".</summary>
    [HttpGet("lookup")]
    public Task<OrderDto> Lookup([FromQuery] string code, CancellationToken ct) => pos.LookupAsync(code, ct);

    /// <summary>The server computes the change due.</summary>
    [HttpPost("{id:guid}/confirm-cash")]
    public Task<CashConfirmationDto> ConfirmCash(Guid id, ConfirmCashRequest request, CancellationToken ct) =>
        pos.ConfirmCashAsync(id, request, User.GetStaffId(), ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<OrderDto> Cancel(Guid id, CancelOrderRequest request, CancellationToken ct) =>
        pos.CancelAsync(id, request, User.GetStaffId(), ct);
}
