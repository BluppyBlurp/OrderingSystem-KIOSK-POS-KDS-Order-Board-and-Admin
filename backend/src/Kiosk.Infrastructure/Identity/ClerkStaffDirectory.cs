using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kiosk.Application.Common;
using Kiosk.Application.Staff;
using Kiosk.Domain.Common;
using Microsoft.Extensions.Options;

namespace Kiosk.Infrastructure.Identity;

public sealed class ClerkBackendOptions
{
    public const string Section = "Clerk";

    /// <summary>sk_test_… / sk_live_… — Backend API key for staff management. Never shipped to a frontend.</summary>
    public string SecretKey { get; set; } = "";

    public string ApiUrl { get; set; } = "https://api.clerk.com/v1/";
}

/// <summary>Staff accounts through Clerk's Backend API (users, metadata, sessions, invitations).</summary>
internal sealed class ClerkStaffDirectory(HttpClient http, IOptions<ClerkBackendOptions> options) : IStaffDirectory
{
    private const int MaxUsers = 500;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.SecretKey);

    public async Task<IReadOnlyList<StaffUser>> ListUsersAsync(CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Get, $"users?limit={MaxUsers}&order_by=-created_at", null, ct);
        return Items(json).Select(ToUser).ToList();
    }

    public async Task<StaffUser?> GetUserAsync(string userId, CancellationToken ct)
    {
        try
        {
            return ToUser(await SendAsync(HttpMethod.Get, $"users/{Uri.EscapeDataString(userId)}", null, ct));
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    // Clerk merges metadata; a null value removes the key.
    public Task SetRoleAsync(string userId, string? role, CancellationToken ct) =>
        SendAsync(HttpMethod.Patch, $"users/{Uri.EscapeDataString(userId)}/metadata", new { public_metadata = new { role } }, ct);

    public async Task RevokeSessionsAsync(string userId, CancellationToken ct)
    {
        var sessions = await SendAsync(HttpMethod.Get, $"sessions?user_id={Uri.EscapeDataString(userId)}&status=active", null, ct);
        foreach (var session in Items(sessions))
            await SendAsync(HttpMethod.Post, $"sessions/{Uri.EscapeDataString(session.GetProperty("id").GetString()!)}/revoke", null, ct);
    }

    public Task DeleteUserAsync(string userId, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"users/{Uri.EscapeDataString(userId)}", null, ct);

    public async Task<IReadOnlyList<StaffInvitation>> ListPendingInvitationsAsync(CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Get, "invitations?status=pending&limit=100", null, ct);
        return Items(json).Select(ToInvitation).ToList();
    }

    public async Task<StaffInvitation> InviteAsync(string email, string role, string? redirectUrl, CancellationToken ct) =>
        ToInvitation(await SendAsync(HttpMethod.Post, "invitations", new
        {
            email_address = email,
            public_metadata = new { role },
            redirect_url = redirectUrl,
            notify = true,
        }, ct));

    public Task RevokeInvitationAsync(string invitationId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"invitations/{Uri.EscapeDataString(invitationId)}/revoke", null, ct);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.SecretKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            if (text.Length == 0)
                return default;
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }

        var message = ErrorMessage(text);
        throw response.StatusCode switch
        {
            HttpStatusCode.NotFound => new NotFoundException(message ?? "Not found in Clerk."),
            // e.g. "already invited", "email already taken": tell the manager, don't hide it as an outage
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict or HttpStatusCode.Forbidden =>
                new DomainException("clerk_rejected", message ?? "Clerk rejected the request."),
            _ => new ServiceUnavailableException($"Clerk is unavailable ({(int)response.StatusCode}). Try again shortly."),
        };
    }

    /// <summary>Clerk errors: {"errors":[{"message":"…","long_message":"…"}]}.</summary>
    internal static string? ErrorMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                return first.TryGetProperty("long_message", out var l) && l.GetString() is { Length: > 0 } longMessage
                    ? longMessage
                    : first.GetProperty("message").GetString();
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    /// <summary>List endpoints answer either a bare array or {"data":[…],"total_count":n}.</summary>
    internal static IEnumerable<JsonElement> Items(JsonElement json) =>
        json.ValueKind == JsonValueKind.Array ? json.EnumerateArray()
        : json.ValueKind == JsonValueKind.Object && json.TryGetProperty("data", out var data) ? data.EnumerateArray()
        : [];

    internal static StaffUser ToUser(JsonElement u)
    {
        var primaryId = Str(u, "primary_email_address_id");
        var emails = u.TryGetProperty("email_addresses", out var e) ? e.EnumerateArray().ToList() : [];
        var email = emails.FirstOrDefault(x => Str(x, "id") == primaryId) is { ValueKind: JsonValueKind.Object } primary
            ? Str(primary, "email_address")
            : emails.Select(x => Str(x, "email_address")).FirstOrDefault();
        var name = $"{Str(u, "first_name")} {Str(u, "last_name")}".Trim();

        return new StaffUser(
            Str(u, "id")!,
            name.Length > 0 ? name : email ?? Str(u, "username") ?? "Unnamed",
            email,
            Role(u),
            Str(u, "image_url"),
            Time(u, "created_at") ?? DateTimeOffset.UnixEpoch,
            Time(u, "last_sign_in_at"),
            u.TryGetProperty("banned", out var b) && b.ValueKind == JsonValueKind.True);
    }

    internal static StaffInvitation ToInvitation(JsonElement i) =>
        new(Str(i, "id")!, Str(i, "email_address") ?? "", Role(i), Time(i, "created_at") ?? DateTimeOffset.UnixEpoch);

    private static string? Role(JsonElement e) =>
        e.TryGetProperty("public_metadata", out var meta) && meta.ValueKind == JsonValueKind.Object &&
        meta.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String
            ? role.GetString()
            : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Clerk timestamps are Unix milliseconds.</summary>
    private static DateTimeOffset? Time(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeMilliseconds(v.GetInt64()) : null;
}
