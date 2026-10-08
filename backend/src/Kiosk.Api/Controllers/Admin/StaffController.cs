using System.Security.Claims;
using Kiosk.Api.Auth;
using Kiosk.Application.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.Api.Controllers.Admin;

/// <summary>
/// Staff accounts: invite by email with a role, approve people who signed up themselves, change roles, remove access.
/// Admins manage everyone; managers manage assistant managers, cashiers and kitchen staff (see <see cref="StaffRoles"/>).
/// </summary>
[ApiController]
[Route("api/admin/staff")]
[Authorize(Policy = Policies.Admin)]
public sealed class StaffController(StaffService staff, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public Task<StaffOverviewDto> Overview(CancellationToken ct) => staff.GetOverviewAsync(Actor, ct);

    /// <summary>Emails a sign-up link. <c>redirectUrl</c> (where to land afterwards) must be one of our app origins.</summary>
    [HttpPost("invitations")]
    public Task<StaffInvitation> Invite(InviteStaffRequest request, CancellationToken ct) =>
        staff.InviteAsync(Actor, request, config.GetSection("Cors:Origins").Get<string[]>() ?? [], ct);

    [HttpPost("invitations/{id}/revoke")]
    public async Task<IActionResult> RevokeInvitation(string id, CancellationToken ct)
    {
        await staff.RevokeInvitationAsync(Actor, id, ct);
        return NoContent();
    }

    /// <summary>Approves a pending sign-up, or changes a staff member's role. A demotion signs them out everywhere.</summary>
    [HttpPut("{userId}/role")]
    public Task<StaffMemberDto> SetRole(string userId, SetStaffRoleRequest request, CancellationToken ct) =>
        staff.SetRoleAsync(Actor, userId, request, ct);

    /// <summary>Removes the role and signs the person out everywhere. They can be approved again later.</summary>
    [HttpPost("{userId}/remove-access")]
    public async Task<IActionResult> RemoveAccess(string userId, CancellationToken ct)
    {
        await staff.RemoveAccessAsync(Actor, userId, ct);
        return NoContent();
    }

    /// <summary>Rejects a sign-up that was never approved (deletes the account).</summary>
    [HttpDelete("{userId}")]
    public async Task<IActionResult> Reject(string userId, CancellationToken ct)
    {
        await staff.RejectAsync(Actor, userId, ct);
        return NoContent();
    }

    private StaffActor Actor => new(User.GetStaffId(), User.FindFirstValue(Claims.Role));
}
