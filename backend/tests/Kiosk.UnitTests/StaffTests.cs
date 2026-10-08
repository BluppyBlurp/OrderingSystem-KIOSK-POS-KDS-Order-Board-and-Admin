using System.Net;
using System.Text;
using System.Text.Json;
using Kiosk.Application.Staff;
using Kiosk.Domain.Common;
using Kiosk.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Kiosk.UnitTests;

public class StaffRolesTests
{
    [Fact]
    public void Admins_assign_anything_managers_only_the_roles_below_them()
    {
        Assert.Equal(StaffRoles.All, StaffRoles.AssignableBy(StaffRoles.Admin));
        Assert.Equal([StaffRoles.AssistantManager, StaffRoles.Cashier, StaffRoles.Kitchen], StaffRoles.AssignableBy(StaffRoles.Manager));
        Assert.Empty(StaffRoles.AssignableBy(StaffRoles.AssistantManager));
        Assert.Empty(StaffRoles.AssignableBy(null));
    }

    [Fact]
    public void Pending_signups_can_be_handled_by_any_manager()
    {
        Assert.True(StaffRoles.CanManage(StaffRoles.Manager, null));
        Assert.False(StaffRoles.CanManage(StaffRoles.Manager, StaffRoles.Manager));
        Assert.True(StaffRoles.CanManage(StaffRoles.Admin, StaffRoles.Admin));
    }
}

public class ClerkStaffDirectoryTests
{
    private const string UserJson = """
        {"id":"user_1","first_name":"Ana","last_name":"Cruz","primary_email_address_id":"idn_2",
         "email_addresses":[{"id":"idn_1","email_address":"old@example.com"},{"id":"idn_2","email_address":"ana@example.com"}],
         "public_metadata":{"role":"cashier"},"image_url":"https://img.clerk.com/a","created_at":1760000000000,"last_sign_in_at":null,"banned":false}
        """;

    [Fact]
    public void Reads_a_clerk_user_with_its_primary_email_and_role()
    {
        var user = ClerkStaffDirectory.ToUser(JsonDocument.Parse(UserJson).RootElement);
        Assert.Equal(("user_1", "Ana Cruz", "ana@example.com", "cashier"), (user.Id, user.Name, user.Email, user.Role));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1760000000000), user.CreatedAt);
        Assert.Null(user.LastSignInAt);
    }

    [Fact]
    public void A_user_without_a_name_or_role_falls_back_to_the_email_and_no_role()
    {
        var user = ClerkStaffDirectory.ToUser(JsonDocument.Parse(
            """{"id":"user_2","email_addresses":[{"id":"e","email_address":"x@example.com"}],"public_metadata":{},"created_at":1}""").RootElement);
        Assert.Equal(("x@example.com", null), (user.Name, user.Role));
    }

    [Theory]
    [InlineData("""[{"id":"a"},{"id":"b"}]""")]
    [InlineData("""{"data":[{"id":"a"},{"id":"b"}],"total_count":2}""")]
    public void List_responses_come_as_arrays_or_paged_objects(string json) =>
        Assert.Equal(2, ClerkStaffDirectory.Items(JsonDocument.Parse(json).RootElement).Count());

    [Fact]
    public void Clerk_error_messages_are_passed_on()
    {
        Assert.Equal("That email is already invited.", ClerkStaffDirectory.ErrorMessage(
            """{"errors":[{"message":"duplicate","long_message":"That email is already invited.","code":"duplicate_record"}]}"""));
        Assert.Null(ClerkStaffDirectory.ErrorMessage("<html>gateway</html>"));
    }

    [Fact]
    public async Task Removing_a_role_sends_null_so_clerk_deletes_the_key()
    {
        var handler = new RecordingHandler("""{"id":"user_1"}""");
        var directory = Directory(handler);

        await directory.SetRoleAsync("user_1", null, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.Method);
        Assert.EndsWith("/v1/users/user_1/metadata", handler.Url);
        Assert.Equal("""{"public_metadata":{"role":null}}""", handler.Body);
        Assert.Equal("Bearer sk_test_unit", handler.Authorization);
    }

    [Fact]
    public async Task A_rejected_invitation_becomes_a_domain_error_with_clerks_message()
    {
        var directory = Directory(new RecordingHandler("""{"errors":[{"message":"taken","long_message":"Already a member."}]}""", HttpStatusCode.UnprocessableEntity));
        var error = await Assert.ThrowsAsync<DomainException>(() => directory.InviteAsync("a@example.com", "cashier", null, CancellationToken.None));
        Assert.Equal("Already a member.", error.Message);
    }

    private static ClerkStaffDirectory Directory(RecordingHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.clerk.test/v1/") },
            Options.Create(new ClerkBackendOptions { SecretKey = "sk_test_unit" }));

    private sealed class RecordingHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string Url { get; private set; } = "";
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method;
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
