using Kiosk.Api.Auth;
using Kiosk.Application.Orders;
using Kiosk.Application.Receipts;
using Kiosk.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Api.Controllers.Pos;

/// <summary>Cashier POS: find the order from the slip, take cash, never retype the order.</summary>
[ApiController]
[Route("api/pos/orders")]
[Authorize(Policy = Policies.Pos)]
public sealed class PosController(PosService pos, ReceiptService receipts) : ControllerBase
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

    /// <summary>Paid receipt as an 80 mm PDF, marked REPRINT.</summary>
    [HttpGet("{id:guid}/receipt")]
    [Produces("application/pdf")]
    public async Task<FileContentResult> Receipt(Guid id, CancellationToken ct)
    {
        var pdf = await receipts.GetReceiptAsync(id, ct);
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}

[ApiController]
[Route("api/pos")]
[Authorize(Policy = Policies.Pos)]
public sealed class PosShiftController(ReportService reports) : ControllerBase
{
    /// <summary>Cash taken since the shift started (default: start of today), for counting the drawer.</summary>
    [HttpGet("shift-summary")]
    public Task<ShiftSummaryDto> ShiftSummary([FromQuery] DateTimeOffset? since, CancellationToken ct) =>
        reports.GetShiftSummaryAsync(User.GetStaffId(), since, ct);
}
