using System.Net;
using System.Reflection;
using Kiosk.Api.Auth;
using Kiosk.Application.Devices;
using Kiosk.Application.Orders;
using Kiosk.Domain.Devices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kiosk.IntegrationTests;

/// <summary>Docs §4: one backend, hard boundaries. Each route group admits only its own callers.</summary>
[Collection(ApiCollection.Name)]
public sealed class SecurityBoundaryTests(KioskApiFactory api)
{
    public static TheoryData<string> StaffOnlyRoutes =>
    [
        "/api/admin/products",
        "/api/admin/devices",
        "/api/pos/orders",
        "/api/kds/orders",
    ];

    [Theory]
    [MemberData(nameof(StaffOnlyRoutes))]
    public async Task Kiosk_device_token_is_forbidden_outside_the_kiosk_group(string route) =>
        (await (await api.DeviceAsync(DeviceKind.Kiosk)).GetAsync(route)).AssertStatus(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("/api/kiosk/menu")]
    [InlineData("/api/pos/orders")]
    [InlineData("/api/admin/products")]
    [InlineData("/api/display/board")]
    public async Task Anonymous_callers_are_rejected(string route) =>
        (await api.CreateClient().GetAsync(route)).AssertStatus(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Revoked_or_made_up_device_tokens_are_rejected()
    {
        (await api.WithBearer("dev_not-a-real-token").GetAsync("/api/kiosk/menu")).AssertStatus(HttpStatusCode.Unauthorized);

        var admin = api.Staff(Roles.Admin);
        var registered = await (await admin.PostJsonAsync("/api/admin/devices", new RegisterDeviceRequest("Kiosk 9", DeviceKind.Kiosk)))
            .ReadAsync<RegisteredDeviceDto>();
        var kiosk = api.WithBearer(registered.Token);
        (await kiosk.GetAsync("/api/kiosk/menu")).AssertStatus(HttpStatusCode.OK);

        (await admin.PostAsync($"/api/admin/devices/{registered.Device.Id}/revoke", null)).AssertStatus(HttpStatusCode.NoContent);
        (await kiosk.GetAsync("/api/kiosk/menu")).AssertStatus(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Roles.Cashier, "/api/admin/products", HttpStatusCode.Forbidden)]
    [InlineData(Roles.Cashier, "/api/kds/orders", HttpStatusCode.Forbidden)]
    [InlineData(Roles.Kitchen, "/api/pos/orders", HttpStatusCode.Forbidden)]
    [InlineData(Roles.Kitchen, "/api/admin/devices", HttpStatusCode.Forbidden)]
    [InlineData(Roles.Cashier, "/api/kiosk/menu", HttpStatusCode.Forbidden)]
    [InlineData(Roles.Cashier, "/api/pos/orders", HttpStatusCode.OK)]
    [InlineData(Roles.Kitchen, "/api/kds/orders", HttpStatusCode.OK)]
    [InlineData(Roles.Kitchen, "/api/display/board", HttpStatusCode.OK)]
    [InlineData(Roles.Manager, "/api/admin/products", HttpStatusCode.OK)]
    [InlineData("intruder", "/api/display/board", HttpStatusCode.Forbidden)]
    public async Task Staff_roles_reach_only_their_apps(string role, string route, HttpStatusCode expected) =>
        (await api.Staff(role).GetAsync(route)).AssertStatus(expected);

    [Theory]
    [InlineData("devstaff_admin")]
    [InlineData("devstaff_cashier")]
    public async Task Dev_staff_tokens_are_rejected_outside_development(string token) =>
        (await api.WithBearer(token).GetAsync("/api/pos/orders")).AssertStatus(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Board_token_cannot_create_orders()
    {
        var board = await api.DeviceAsync(DeviceKind.Board);
        (await board.OrderAsync(Guid.NewGuid())).AssertStatus(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_kiosk_cannot_read_another_kiosks_order()
    {
        var coffee = await api.CreateProductAsync("Coffee", 55m, stock: null);
        var kioskA = await api.DeviceAsync(DeviceKind.Kiosk);
        var kioskB = await api.DeviceAsync(DeviceKind.Kiosk);
        var order = await (await kioskA.OrderAsync(coffee.Id)).ReadAsync<KioskOrderDto>();

        (await kioskB.GetAsync($"/api/kiosk/orders/{order.Order.Id}")).AssertStatus(HttpStatusCode.NotFound);
        (await kioskB.PostJsonAsync($"/api/kiosk/orders/{order.Order.Id}/pay", new PayRequest(Domain.Orders.PaymentMethod.Cash)))
            .AssertStatus(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Every_controller_action_declares_a_policy_or_is_explicitly_anonymous()
    {
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var unguarded = controllers
            .SelectMany(c => c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(m => (Controller: c, Method: m)))
            .Where(x =>
            {
                var attrs = x.Controller.GetCustomAttributes(true).Concat(x.Method.GetCustomAttributes(true)).ToList();
                return !attrs.OfType<AllowAnonymousAttribute>().Any()
                       && !attrs.OfType<AuthorizeAttribute>().Any(a => !string.IsNullOrEmpty(a.Policy));
            })
            .Select(x => $"{x.Controller.Name}.{x.Method.Name}")
            .ToList();

        Assert.Empty(unguarded);
    }
}
