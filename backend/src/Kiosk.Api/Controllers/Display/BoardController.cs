using Kiosk.Api.Auth;
using Kiosk.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Api.Controllers.Display;

/// <summary>Public "Preparing / Now Serving" screen. Read-only; numbers and table stands only.</summary>
[ApiController]
[Route("api/display")]
[Authorize(Policy = Policies.Board)]
public sealed class BoardController(BoardService board) : ControllerBase
{
    [HttpGet("board")]
    public Task<BoardDto> Get(CancellationToken ct) => board.GetAsync(ct);
}
