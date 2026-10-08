using System.Collections.Concurrent;
using Kiosk.Application.Staff;

namespace Kiosk.IntegrationTests;

/// <summary>Stands in for Clerk's Backend API. Records sign-outs so tests can check that demotions take effect now.</summary>
public sealed class FakeStaffDirectory : IStaffDirectory
{
    public ConcurrentDictionary<string, StaffUser> Users { get; } = new();
    public ConcurrentDictionary<string, StaffInvitation> Invitations { get; } = new();
    public ConcurrentBag<string> SignedOut { get; } = [];
    public string? LastRedirectUrl { get; private set; }
    public bool IsConfigured { get; set; } = true;

    public StaffUser Add(string id, string? role, string name = "Test Person")
    {
        var user = new StaffUser(id, name, $"{id}@example.com", role, null, DateTimeOffset.UtcNow, null, false);
        Users[id] = user;
        return user;
    }

    public Task<IReadOnlyList<StaffUser>> ListUsersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<StaffUser>>(Users.Values.ToList());

    public Task<StaffUser?> GetUserAsync(string userId, CancellationToken ct) => Task.FromResult(Users.GetValueOrDefault(userId));

    public Task SetRoleAsync(string userId, string? role, CancellationToken ct)
    {
        Users[userId] = Users[userId] with { Role = role };
        return Task.CompletedTask;
    }

    public Task RevokeSessionsAsync(string userId, CancellationToken ct)
    {
        SignedOut.Add(userId);
        return Task.CompletedTask;
    }

    public Task DeleteUserAsync(string userId, CancellationToken ct)
    {
        Users.TryRemove(userId, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StaffInvitation>> ListPendingInvitationsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StaffInvitation>>(Invitations.Values.ToList());

    public Task<StaffInvitation> InviteAsync(string email, string role, string? redirectUrl, CancellationToken ct)
    {
        LastRedirectUrl = redirectUrl;
        var invitation = new StaffInvitation($"inv_{Guid.NewGuid():N}", email, role, DateTimeOffset.UtcNow);
        Invitations[invitation.Id] = invitation;
        return Task.FromResult(invitation);
    }

    public Task RevokeInvitationAsync(string invitationId, CancellationToken ct)
    {
        Invitations.TryRemove(invitationId, out _);
        return Task.CompletedTask;
    }
}
