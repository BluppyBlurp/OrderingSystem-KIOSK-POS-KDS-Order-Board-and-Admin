using System.Security.Claims;
using Kiosk.Domain.Devices;
using Microsoft.AspNetCore.Authorization;

namespace Kiosk.Api.Auth;

/// <summary>One policy per route group (docs §4, §11). Every controller must name one.</summary>
public static class Policies
{
    public const string Kiosk = nameof(Kiosk);
    public const string Board = nameof(Board);
    public const string Pos = nameof(Pos);
    public const string Kds = nameof(Kds);
    public const string Admin = nameof(Admin);

    /// <summary>Day-to-day back office: read the menu, mark items sold out / in stock, read reports. Includes assistant managers.</summary>
    public const string BackOffice = nameof(BackOffice);
    public const string AnyClient = nameof(AnyClient);

    public static void Register(AuthorizationOptions options)
    {
        options.AddPolicy(Kiosk, p => p.RequireClaim(Claims.DeviceKind, nameof(DeviceKind.Kiosk)));
        options.AddPolicy(Board, p => p.RequireAssertion(ctx =>
            ctx.User.HasClaim(Claims.DeviceKind, nameof(DeviceKind.Board)) ||
            ctx.User.HasAnyRole(Roles.AllStaff)));
        options.AddPolicy(Pos, p => p.RequireClaim(Claims.Role, Roles.Admin, Roles.Manager, Roles.AssistantManager, Roles.Cashier));
        options.AddPolicy(Kds, p => p.RequireClaim(Claims.Role, Roles.Admin, Roles.Manager, Roles.AssistantManager, Roles.Kitchen));
        options.AddPolicy(Admin, p => p.RequireClaim(Claims.Role, Roles.Admin, Roles.Manager));
        options.AddPolicy(BackOffice, p => p.RequireClaim(Claims.Role, Roles.Admin, Roles.Manager, Roles.AssistantManager));
        options.AddPolicy(AnyClient, p => p.RequireAuthenticatedUser());

        // Default-deny: an endpoint that forgot [Authorize(Policy = …)] or [AllowAnonymous] is unreachable.
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAssertion(_ => false).Build();
    }
}

/// <summary>Role claim values; the list itself lives in <see cref="Kiosk.Application.Staff.StaffRoles"/>.</summary>
public static class Roles
{
    public const string Admin = Kiosk.Application.Staff.StaffRoles.Admin;
    public const string Manager = Kiosk.Application.Staff.StaffRoles.Manager;
    public const string AssistantManager = Kiosk.Application.Staff.StaffRoles.AssistantManager;
    public const string Cashier = Kiosk.Application.Staff.StaffRoles.Cashier;
    public const string Kitchen = Kiosk.Application.Staff.StaffRoles.Kitchen;

    public static readonly string[] AllStaff = [.. Kiosk.Application.Staff.StaffRoles.All];
}

public static class Claims
{
    /// <summary>Clerk session token custom claim: "role": "{{user.public_metadata.role}}".</summary>
    public const string Role = "role";
    public const string Subject = "sub";
    public const string DeviceId = "device_id";
    public const string DeviceKind = "device_kind";

    public static bool HasAnyRole(this ClaimsPrincipal user, params string[] roles) =>
        user.FindAll(Role).Any(c => roles.Contains(c.Value));

    public static Guid GetDeviceId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(DeviceId), out var id)
            ? id
            : throw new InvalidOperationException("Request is not authenticated as a device.");

    public static string GetStaffId(this ClaimsPrincipal user) =>
        user.FindFirstValue(Subject) ?? throw new InvalidOperationException("Request is not authenticated as staff.");
}
