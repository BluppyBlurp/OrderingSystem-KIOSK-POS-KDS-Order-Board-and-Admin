using Kiosk.Api.Auth;
using Kiosk.Application.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Api.Controllers.Admin;

/// <summary>Register kiosks and order boards. The token is shown once; enter it on the device.</summary>
[ApiController]
[Route("api/admin/devices")]
[Authorize(Policy = Policies.Admin)]
public sealed class DevicesController(DeviceService devices) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DeviceDto>> List(CancellationToken ct) => devices.ListAsync(ct);

    [HttpPost]
    public Task<RegisteredDeviceDto> Register(RegisterDeviceRequest r, CancellationToken ct) => devices.RegisterAsync(r, ct);

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await devices.RevokeAsync(id, ct);
        return NoContent();
    }
}
