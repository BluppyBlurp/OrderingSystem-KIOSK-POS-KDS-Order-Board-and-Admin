using Kiosk.Api.Auth;
using Kiosk.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Api.Controllers.Kds;

/// <summary>Kitchen display: paid orders in, Preparing → Ready → Handed Over.</summary>
[ApiController]
[Route("api/kds/orders")]
[Authorize(Policy = Policies.Kds)]
public sealed class KdsController(KdsService kds) : ControllerBase
{
    /// <summary>Paid, Preparing and Ready tickets, oldest payment first.</summary>
    [HttpGet]
    public Task<IReadOnlyList<OrderDto>> GetActive(CancellationToken ct) => kds.GetActiveAsync(ct);

    [HttpPost("{id:guid}/preparing")]
    public Task<OrderDto> StartPreparing(Guid id, CancellationToken ct) => kds.StartPreparingAsync(id, User.GetStaffId(), ct);

    [HttpPost("{id:guid}/ready")]
    public Task<OrderDto> MarkReady(Guid id, CancellationToken ct) => kds.MarkReadyAsync(id, User.GetStaffId(), ct);

    [HttpPost("{id:guid}/complete")]
    public Task<OrderDto> Complete(Guid id, CancellationToken ct) => kds.CompleteAsync(id, User.GetStaffId(), ct);
}
