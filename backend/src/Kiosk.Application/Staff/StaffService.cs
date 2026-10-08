using System.Net.Mail;
using System.Text.Json;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Domain.Auditing;
using Kiosk.Domain.Common;

namespace Kiosk.Application.Staff;

public sealed record StaffUser(
    string Id,
    string Name,
    string? Email,
    string? Role,
    string? ImageUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt,
    bool Banned);

public sealed record StaffInvitation(string Id, string Email, string? Role, DateTimeOffset CreatedAt);

/// <summary>
/// Staff accounts live in Clerk (identity), not in our database. Roles are Clerk public metadata, which Clerk copies
/// into each session token, so the API never looks a user up per request.
/// </summary>
public interface IStaffDirectory
{
    /// <summary>False until the Clerk secret key is configured; staff endpoints then answer 503.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<StaffUser>> ListUsersAsync(CancellationToken ct);
    Task<StaffUser?> GetUserAsync(string userId, CancellationToken ct);

    /// <summary><c>null</c> removes the role (no access).</summary>
    Task SetRoleAsync(string userId, string? role, CancellationToken ct);

    /// <summary>Signs the user out everywhere, so a role change takes effect now rather than at the next token refresh.</summary>
    Task RevokeSessionsAsync(string userId, CancellationToken ct);

    Task DeleteUserAsync(string userId, CancellationToken ct);

    Task<IReadOnlyList<StaffInvitation>> ListPendingInvitationsAsync(CancellationToken ct);

    /// <summary>Emails a sign-up link; the account gets <paramref name="role"/> as soon as it is created.</summary>
    Task<StaffInvitation> InviteAsync(string email, string role, string? redirectUrl, CancellationToken ct);

    Task RevokeInvitationAsync(string invitationId, CancellationToken ct);
}

public sealed record StaffMemberDto(
    string Id,
    string Name,
    string? Email,
    string? Role,
    string? ImageUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt,
    bool CanManage,
    bool IsYou);

/// <summary>Everyone the caller can see, split into staff with a role and sign-ups waiting for approval.</summary>
public sealed record StaffOverviewDto(
    IReadOnlyList<StaffMemberDto> Staff,
    IReadOnlyList<StaffMemberDto> PendingApproval,
    IReadOnlyList<StaffInvitation> Invitations,
    IReadOnlyList<string> AssignableRoles);

public sealed record InviteStaffRequest(string Email, string Role, string? RedirectUrl);

public sealed record SetStaffRoleRequest(string Role);

public sealed record StaffActor(string UserId, string? Role);

/// <summary>
/// Staff management for the Admin app: invite by email with a role, approve self sign-ups by giving them a role,
/// change roles, and remove access. Rules: admins manage everyone; managers manage assistant managers, cashiers and
/// kitchen staff; nobody changes their own role. Every change is written to the audit log.
/// </summary>
public sealed class StaffService(IStaffDirectory directory, IAppDbContext db, BusinessClock clock)
{
    public async Task<StaffOverviewDto> GetOverviewAsync(StaffActor actor, CancellationToken ct)
    {
        EnsureConfigured();
        var users = await directory.ListUsersAsync(ct);
        var invitations = await directory.ListPendingInvitationsAsync(ct);

        StaffMemberDto Dto(StaffUser u) => new(
            u.Id, u.Name, u.Email, StaffRoles.IsKnown(u.Role) ? u.Role : null, u.ImageUrl, u.CreatedAt, u.LastSignInAt,
            CanManage: u.Id != actor.UserId && StaffRoles.CanManage(actor.Role, StaffRoles.IsKnown(u.Role) ? u.Role : null),
            IsYou: u.Id == actor.UserId);

        var active = users.Where(u => !u.Banned).ToList();
        return new StaffOverviewDto(
            active.Where(u => StaffRoles.IsKnown(u.Role)).OrderBy(u => StaffRoles.All.ToList().IndexOf(u.Role!)).ThenBy(u => u.Name).Select(Dto).ToList(),
            active.Where(u => !StaffRoles.IsKnown(u.Role)).OrderByDescending(u => u.CreatedAt).Select(Dto).ToList(),
            invitations,
            StaffRoles.AssignableBy(actor.Role));
    }

    public async Task<StaffInvitation> InviteAsync(StaffActor actor, InviteStaffRequest request, IReadOnlyCollection<string> allowedRedirectOrigins, CancellationToken ct)
    {
        EnsureConfigured();
        EnsureMayAssign(actor, request.Role);
        var email = NormalizeEmail(request.Email);

        // Only send people back to one of our own apps after they accept.
        var redirect = request.RedirectUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                       allowedRedirectOrigins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase)
            ? url
            : null;

        var invitation = await directory.InviteAsync(email, request.Role, redirect, ct);
        await AuditAsync(actor, "StaffInvitation", invitation.Id, "Invited", null, new { email, role = request.Role }, ct);
        return invitation;
    }

    public async Task RevokeInvitationAsync(StaffActor actor, string invitationId, CancellationToken ct)
    {
        EnsureConfigured();
        var invitation = (await directory.ListPendingInvitationsAsync(ct)).FirstOrDefault(i => i.Id == invitationId)
                         ?? throw new NotFoundException("Invitation not found or already used.");
        if (!StaffRoles.CanManage(actor.Role, invitation.Role))
            throw new DomainException("not_allowed", "You can't cancel an invitation for that role.");

        await directory.RevokeInvitationAsync(invitationId, ct);
        await AuditAsync(actor, "StaffInvitation", invitationId, "Revoked", new { invitation.Email, invitation.Role }, null, ct);
    }

    /// <summary>Approves a pending sign-up, or changes an existing staff member's role.</summary>
    public async Task<StaffMemberDto> SetRoleAsync(StaffActor actor, string userId, SetStaffRoleRequest request, CancellationToken ct)
    {
        EnsureConfigured();
        var user = await GetManageableAsync(actor, userId, ct);
        EnsureMayAssign(actor, request.Role);

        var before = StaffRoles.IsKnown(user.Role) ? user.Role : null;
        await directory.SetRoleAsync(userId, request.Role, ct);
        // A demotion must not wait for the old token to expire.
        if (before is not null && before != request.Role)
            await directory.RevokeSessionsAsync(userId, ct);
        await AuditAsync(actor, "StaffUser", userId, before is null ? "Approved" : "RoleChanged", new { role = before }, new { role = request.Role }, ct);

        return new StaffMemberDto(user.Id, user.Name, user.Email, request.Role, user.ImageUrl, user.CreatedAt, user.LastSignInAt, true, false);
    }

    /// <summary>Takes the role away and signs the person out everywhere. Their Clerk account stays, so they can be re-approved.</summary>
    public async Task RemoveAccessAsync(StaffActor actor, string userId, CancellationToken ct)
    {
        EnsureConfigured();
        var user = await GetManageableAsync(actor, userId, ct);
        await directory.SetRoleAsync(userId, null, ct);
        await directory.RevokeSessionsAsync(userId, ct);
        await AuditAsync(actor, "StaffUser", userId, "AccessRemoved", new { role = user.Role }, new { role = (string?)null }, ct);
    }

    /// <summary>Rejects a sign-up that was never approved: the account is deleted.</summary>
    public async Task RejectAsync(StaffActor actor, string userId, CancellationToken ct)
    {
        EnsureConfigured();
        var user = await GetManageableAsync(actor, userId, ct);
        if (StaffRoles.IsKnown(user.Role))
            throw new DomainException("has_role", "This person already has access. Use Remove access instead.");
        await directory.DeleteUserAsync(userId, ct);
        await AuditAsync(actor, "StaffUser", userId, "Rejected", new { user.Email }, null, ct);
    }

    private async Task<StaffUser> GetManageableAsync(StaffActor actor, string userId, CancellationToken ct)
    {
        if (userId == actor.UserId)
            throw new DomainException("not_yourself", "You can't change your own access. Ask another admin.");
        var user = await directory.GetUserAsync(userId, ct) ?? throw new NotFoundException("Staff member not found.");
        if (!StaffRoles.CanManage(actor.Role, StaffRoles.IsKnown(user.Role) ? user.Role : null))
            throw new DomainException("not_allowed", $"Only an admin can change a {user.Role}.");
        return user;
    }

    private static void EnsureMayAssign(StaffActor actor, string role)
    {
        if (!StaffRoles.IsKnown(role))
            throw new DomainException("unknown_role", $"Unknown role \"{role}\".");
        if (!StaffRoles.AssignableBy(actor.Role).Contains(role))
            throw new DomainException("not_allowed", $"You can't give people the {role} role.");
    }

    private static string NormalizeEmail(string email)
    {
        var trimmed = email.Trim();
        return MailAddress.TryCreate(trimmed, out var parsed) && parsed.Address == trimmed
            ? trimmed.ToLowerInvariant()
            : throw new DomainException("invalid_email", "Enter a valid email address.");
    }

    private void EnsureConfigured()
    {
        if (!directory.IsConfigured)
            throw new ServiceUnavailableException("Staff management is not configured yet. Set Clerk:SecretKey on the API.");
    }

    private async Task AuditAsync(StaffActor actor, string entityType, string entityId, string action, object? before, object? after, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            At = clock.Now,
            ActorId = actor.UserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Before = before is null ? null : JsonSerializer.Serialize(before),
            After = after is null ? null : JsonSerializer.Serialize(after),
        });
        await db.SaveChangesAsync(ct);
    }
}
