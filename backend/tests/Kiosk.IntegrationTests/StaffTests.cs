using System.Net;
using System.Net.Http.Json;
using Kiosk.Api.Auth;
using Kiosk.Application.Admin;
using Kiosk.Application.Staff;
using Kiosk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kiosk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class StaffTests(KioskApiFactory api)
{
    private FakeStaffDirectory Directory => api.StaffDirectory;

    /// <summary>A signed-in staff client plus the matching directory entry (the token's sub is "{id}_{role}").</summary>
    private (HttpClient Client, string Id) Member(string role)
    {
        var id = $"u{Guid.NewGuid():N}";
        Directory.Add($"{id}_{role}", role);
        return (api.Staff(role, id), $"{id}_{role}");
    }

    [Fact]
    public async Task Admin_invites_by_email_with_a_role_and_can_cancel_it()
    {
        var (admin, _) = Member(Roles.Admin);

        var invitation = await (await admin.PostJsonAsync("/api/admin/staff/invitations",
            new InviteStaffRequest("  New.Cashier@Example.com ", Roles.Cashier, "https://admin.example.com/staff"))).ReadAsync<StaffInvitation>();
        Assert.Equal("new.cashier@example.com", invitation.Email);
        Assert.Equal(Roles.Cashier, invitation.Role);
        Assert.Equal("https://admin.example.com/staff", Directory.LastRedirectUrl);

        var overview = await (await admin.GetAsync("/api/admin/staff")).ReadAsync<StaffOverviewDto>();
        Assert.Contains(overview.Invitations, i => i.Id == invitation.Id);
        Assert.Equal(StaffRoles.All, overview.AssignableRoles);

        (await admin.PostAsync($"/api/admin/staff/invitations/{invitation.Id}/revoke", null)).AssertStatus(HttpStatusCode.NoContent);
        Assert.False(Directory.Invitations.ContainsKey(invitation.Id));
    }

    [Fact]
    public async Task Invitations_only_redirect_to_our_own_apps_and_need_a_real_email()
    {
        var (admin, _) = Member(Roles.Admin);
        await (await admin.PostJsonAsync("/api/admin/staff/invitations",
            new InviteStaffRequest("a@example.com", Roles.Kitchen, "https://evil.example.net/phish"))).ReadAsync<StaffInvitation>();
        Assert.Null(Directory.LastRedirectUrl);

        (await admin.PostJsonAsync("/api/admin/staff/invitations", new InviteStaffRequest("not an email", Roles.Kitchen, null)))
            .AssertStatus(HttpStatusCode.Conflict);
        (await admin.PostJsonAsync("/api/admin/staff/invitations", new InviteStaffRequest("b@example.com", "owner", null)))
            .AssertStatus(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Managers_manage_only_the_roles_below_them()
    {
        var (manager, _) = Member(Roles.Manager);
        var otherAdmin = Directory.Add($"admin_{Guid.NewGuid():N}", Roles.Admin);
        var cashier = Directory.Add($"cashier_{Guid.NewGuid():N}", Roles.Cashier);

        (await manager.PostJsonAsync("/api/admin/staff/invitations", new InviteStaffRequest("m@example.com", Roles.Manager, null)))
            .AssertStatus(HttpStatusCode.Conflict);
        await (await manager.PostJsonAsync("/api/admin/staff/invitations",
            new InviteStaffRequest("am@example.com", Roles.AssistantManager, null))).ReadAsync<StaffInvitation>();

        (await manager.PutAsync($"/api/admin/staff/{otherAdmin.Id}/role", Json(new SetStaffRoleRequest(Roles.Cashier))))
            .AssertStatus(HttpStatusCode.Conflict);
        (await manager.PutAsync($"/api/admin/staff/{cashier.Id}/role", Json(new SetStaffRoleRequest(Roles.Admin))))
            .AssertStatus(HttpStatusCode.Conflict);
        await (await manager.PutAsync($"/api/admin/staff/{cashier.Id}/role", Json(new SetStaffRoleRequest(Roles.Kitchen))))
            .ReadAsync<StaffMemberDto>();

        var overview = await (await manager.GetAsync("/api/admin/staff")).ReadAsync<StaffOverviewDto>();
        Assert.False(Assert.Single(overview.Staff, s => s.Id == otherAdmin.Id).CanManage);
        Assert.True(Assert.Single(overview.Staff, s => s.Id == cashier.Id).CanManage);
        Assert.DoesNotContain(Roles.Manager, overview.AssignableRoles);
    }

    [Fact]
    public async Task A_self_signup_waits_for_approval_then_gets_a_role()
    {
        var (admin, adminId) = Member(Roles.Admin);
        var newcomer = Directory.Add($"signup_{Guid.NewGuid():N}", role: null, name: "New Person");

        var overview = await (await admin.GetAsync("/api/admin/staff")).ReadAsync<StaffOverviewDto>();
        Assert.Contains(overview.PendingApproval, p => p.Id == newcomer.Id);
        Assert.DoesNotContain(overview.Staff, p => p.Id == newcomer.Id);

        var approved = await (await admin.PutAsync($"/api/admin/staff/{newcomer.Id}/role", Json(new SetStaffRoleRequest(Roles.Cashier))))
            .ReadAsync<StaffMemberDto>();
        Assert.Equal(Roles.Cashier, approved.Role);
        Assert.Equal(Roles.Cashier, Directory.Users[newcomer.Id].Role);
        Assert.DoesNotContain(newcomer.Id, Directory.SignedOut); // approval needs no sign-out

        await using var scope = api.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs
            .SingleAsync(a => a.EntityId == newcomer.Id, TestContext.Current.CancellationToken);
        Assert.Equal(("Approved", adminId), (audit.Action, audit.ActorId));
    }

    [Fact]
    public async Task Demotion_and_removal_sign_the_person_out_and_rejection_deletes_only_pending_signups()
    {
        var (admin, _) = Member(Roles.Admin);
        var manager = Directory.Add($"mgr_{Guid.NewGuid():N}", Roles.Manager);
        var kitchen = Directory.Add($"kit_{Guid.NewGuid():N}", Roles.Kitchen);
        var pending = Directory.Add($"pend_{Guid.NewGuid():N}", null);

        await (await admin.PutAsync($"/api/admin/staff/{manager.Id}/role", Json(new SetStaffRoleRequest(Roles.AssistantManager))))
            .ReadAsync<StaffMemberDto>();
        Assert.Contains(manager.Id, Directory.SignedOut);

        (await admin.PostAsync($"/api/admin/staff/{kitchen.Id}/remove-access", null)).AssertStatus(HttpStatusCode.NoContent);
        Assert.Null(Directory.Users[kitchen.Id].Role);
        Assert.Contains(kitchen.Id, Directory.SignedOut);

        (await admin.DeleteAsync($"/api/admin/staff/{manager.Id}")).AssertStatus(HttpStatusCode.Conflict); // has a role
        (await admin.DeleteAsync($"/api/admin/staff/{pending.Id}")).AssertStatus(HttpStatusCode.NoContent);
        Assert.False(Directory.Users.ContainsKey(pending.Id));
    }

    [Fact]
    public async Task Nobody_changes_their_own_access()
    {
        var (admin, adminId) = Member(Roles.Admin);
        (await admin.PutAsync($"/api/admin/staff/{adminId}/role", Json(new SetStaffRoleRequest(Roles.Cashier)))).AssertStatus(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/api/admin/staff/{adminId}/remove-access", null)).AssertStatus(HttpStatusCode.Conflict);

        var overview = await (await admin.GetAsync("/api/admin/staff")).ReadAsync<StaffOverviewDto>();
        var me = Assert.Single(overview.Staff, s => s.Id == adminId);
        Assert.True(me.IsYou);
        Assert.False(me.CanManage);
    }

    [Theory]
    [InlineData(Roles.AssistantManager)]
    [InlineData(Roles.Cashier)]
    [InlineData(Roles.Kitchen)]
    public async Task Only_admins_and_managers_reach_staff_management(string role) =>
        (await api.Staff(role).GetAsync("/api/admin/staff")).AssertStatus(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Without_a_clerk_secret_key_staff_management_answers_503()
    {
        Directory.IsConfigured = false;
        try
        {
            (await api.Staff(Roles.Admin).GetAsync("/api/admin/staff")).AssertStatus(HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            Directory.IsConfigured = true;
        }
    }

    [Fact]
    public async Task Assistant_managers_run_the_floor_but_cannot_change_the_menu()
    {
        var product = await api.CreateProductAsync("Assistant Pie", 45m, stock: 3);
        var assistant = api.Staff(Roles.AssistantManager);

        (await assistant.GetAsync("/api/pos/orders")).AssertStatus(HttpStatusCode.OK);
        (await assistant.GetAsync("/api/kds/orders")).AssertStatus(HttpStatusCode.OK);
        (await assistant.GetAsync("/api/display/board")).AssertStatus(HttpStatusCode.OK);
        (await assistant.GetAsync("/api/admin/products")).AssertStatus(HttpStatusCode.OK);
        (await assistant.GetAsync("/api/admin/reports/sales")).AssertStatus(HttpStatusCode.OK);

        (await assistant.PatchAsync($"/api/admin/products/{product.Id}/availability", Json(new SetAvailabilityRequest(false))))
            .AssertStatus(HttpStatusCode.OK);
        (await assistant.PatchAsync($"/api/admin/products/{product.Id}/stock", Json(new SetStockRequest(10)))).AssertStatus(HttpStatusCode.OK);

        (await assistant.PutAsync($"/api/admin/products/{product.Id}",
            Json(new UpsertProductRequest(product.CategoryId, "Cheaper Pie", null, 1m, null, true, 0, null)))).AssertStatus(HttpStatusCode.Forbidden);
        (await assistant.PostJsonAsync("/api/admin/categories", new UpsertCategoryRequest("Nope", 0, true))).AssertStatus(HttpStatusCode.Forbidden);
        (await assistant.GetAsync("/api/admin/devices")).AssertStatus(HttpStatusCode.Forbidden);
        (await assistant.PostJsonAsync("/api/admin/media/presign", new PresignMediaRequest(Kiosk.Domain.Menu.MediaType.Image, "image/png", 10)))
            .AssertStatus(HttpStatusCode.Forbidden);
    }

    private static JsonContent Json<T>(T body) => JsonContent.Create(body, options: KioskApiFactory.Json);
}
