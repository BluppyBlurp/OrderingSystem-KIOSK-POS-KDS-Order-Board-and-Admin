namespace Kiosk.Application.Staff;

/// <summary>
/// Staff roles, stored in Clerk as <c>publicMetadata.role</c> and copied into the session token's <c>role</c> claim.
/// A signed-up user with no role has no access until a manager approves them.
/// </summary>
public static class StaffRoles
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string AssistantManager = "assistant_manager";
    public const string Cashier = "cashier";
    public const string Kitchen = "kitchen";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, AssistantManager, Cashier, Kitchen];

    public static bool IsKnown(string? role) => role is not null && All.Contains(role);

    /// <summary>
    /// Who may hand out which role. Admins: any. Managers: the roles below them. Nobody else manages staff.
    /// Managers can't create other managers, so promotion to manager stays with the owner.
    /// </summary>
    public static IReadOnlyList<string> AssignableBy(string? actorRole) => actorRole switch
    {
        Admin => All,
        Manager => [AssistantManager, Cashier, Kitchen],
        _ => [],
    };

    /// <summary>Whether <paramref name="actorRole"/> may change or remove someone who currently has <paramref name="targetRole"/>.</summary>
    public static bool CanManage(string? actorRole, string? targetRole) =>
        targetRole is null || AssignableBy(actorRole).Contains(targetRole);
}
