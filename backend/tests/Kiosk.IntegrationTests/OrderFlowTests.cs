using System.Net;
using System.Net.Http.Json;
using Kiosk.Api.Auth;
using Kiosk.Application.Orders;
using Kiosk.Application.Payments;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace Kiosk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class OrderFlowTests(KioskApiFactory api)
{
    [Fact]
    public async Task Cash_order_flows_from_kiosk_to_counter_to_kitchen_to_board()
    {
        var burger = await api.CreateProductAsync("Burger", 142.50m, stock: 10);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var cashier = api.Staff(Roles.Cashier);
        var kitchen = api.Staff(Roles.Kitchen);
        var board = await api.DeviceAsync(DeviceKind.Board);

        // Kiosk: create (stock reserved) then choose cash.
        var created = await (await kiosk.OrderAsync(burger.Id, quantity: 2, OrderType.ServeToTable, table: 12)).ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.Created, created.Order.Status);
        Assert.Equal(285.00m, created.Order.Total);
        Assert.Equal(30.54m, created.Order.TaxAmount);
        Assert.Matches(@"^A-\d+$", created.Order.OrderNumber);
        Assert.Equal(8, await api.StockAsync(burger.Id));

        var slip = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{created.Order.Id}/pay", new PayRequest(PaymentMethod.Cash)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.AwaitingPayment, slip.Order.Status);
        Assert.False(string.IsNullOrEmpty(slip.SlipToken));

        // POS: the order is in the pending queue and found by scanning the QR, or by typing the number.
        var pending = await (await cashier.GetAsync("/api/pos/orders")).ReadAsync<List<OrderDto>>();
        Assert.Contains(pending, o => o.Id == created.Order.Id);
        var scanned = await (await cashier.GetAsync($"/api/pos/orders/lookup?code={Uri.EscapeDataString(slip.SlipToken!)}")).ReadAsync<OrderDto>();
        Assert.Equal(created.Order.Id, scanned.Id);
        var typed = await (await cashier.GetAsync($"/api/pos/orders/lookup?code={created.Order.OrderNumber.Replace("A-", "")}")).ReadAsync<OrderDto>();
        Assert.Equal(created.Order.Id, typed.Id);

        // POS: take ₱500, server computes ₱215 change.
        var paid = await (await cashier.PostJsonAsync($"/api/pos/orders/{created.Order.Id}/confirm-cash", new ConfirmCashRequest(500m)))
            .ReadAsync<CashConfirmationDto>();
        Assert.Equal(215.00m, paid.ChangeDue);
        Assert.Equal(OrderStatus.Paid, paid.Order.Status);
        Assert.Equal(8, await api.StockAsync(burger.Id)); // reserved at creation, not decremented again

        // Kitchen sees it with the table number, and moves it through.
        var tickets = await (await kitchen.GetAsync("/api/kds/orders")).ReadAsync<List<OrderDto>>();
        Assert.Equal(12, Assert.Single(tickets, t => t.Id == created.Order.Id).TableNumber);
        await (await kitchen.PostAsync($"/api/kds/orders/{created.Order.Id}/preparing", null)).ReadAsync<OrderDto>();
        await (await kitchen.PostAsync($"/api/kds/orders/{created.Order.Id}/ready", null)).ReadAsync<OrderDto>();

        var screen = await (await board.GetAsync("/api/display/board")).ReadAsync<BoardDto>();
        Assert.Contains(screen.Ready, e => e.OrderNumber == created.Order.OrderNumber && e.TableNumber == 12);

        var done = await (await kitchen.PostAsync($"/api/kds/orders/{created.Order.Id}/complete", null)).ReadAsync<OrderDto>();
        Assert.Equal(OrderStatus.Completed, done.Status);

        // Ready → Paid is illegal: rejected by the domain, nothing written.
        (await kitchen.PostAsync($"/api/kds/orders/{created.Order.Id}/ready", null)).AssertStatus(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Ewallet_order_is_paid_only_by_the_webhook_and_duplicates_are_no_ops()
    {
        var meal = await api.CreateProductAsync("Meal", 199m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);

        var created = await (await kiosk.OrderAsync(meal.Id)).ReadAsync<KioskOrderDto>();
        var checkout = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{created.Order.Id}/pay", new PayRequest(PaymentMethod.EWallet)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.PaymentPending, checkout.Order.Status);
        Assert.NotNull(checkout.CheckoutUrl);

        var sessionId = checkout.CheckoutUrl!.Split('/').Last();
        var eventId = $"evt_{Guid.NewGuid():N}";

        var first = await (await api.PayMongoPaidAsync(eventId, sessionId, 199m)).ReadAsync<Dictionary<string, string>>();
        Assert.Equal(nameof(WebhookOutcome.Applied), first["outcome"]);

        var replay = await (await api.PayMongoPaidAsync(eventId, sessionId, 199m)).ReadAsync<Dictionary<string, string>>();
        Assert.Equal(nameof(WebhookOutcome.Duplicate), replay["outcome"]);

        var polled = await (await kiosk.GetAsync($"/api/kiosk/orders/{created.Order.Id}")).ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.Paid, polled.Order.Status);
    }

    [Fact]
    public async Task Webhook_with_a_bad_signature_is_rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paymongo")
        {
            Content = JsonContent.Create(new { data = new { id = "evt_forged" } }),
        };
        request.Headers.Add("Paymongo-Signature", "t=1,te=deadbeef");
        (await api.CreateClient().SendAsync(request)).AssertStatus(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_kiosks_racing_for_the_last_item_produce_exactly_one_order()
    {
        var pie = await api.CreateProductAsync("Last Pie", 45m, stock: 1);
        var kioskA = await api.DeviceAsync(DeviceKind.Kiosk);
        var kioskB = await api.DeviceAsync(DeviceKind.Kiosk);

        var responses = await Task.WhenAll(kioskA.OrderAsync(pie.Id), kioskB.OrderAsync(pie.Id));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(0, await api.StockAsync(pie.Id));
    }

    [Fact]
    public async Task Prices_in_the_request_body_are_ignored()
    {
        var fries = await api.CreateProductAsync("Fries", 60m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);

        var tampered = new
        {
            orderType = "CounterPickup",
            total = 1,
            items = new[] { new { productId = fries.Id, quantity = 3, price = 0.01, unitPrice = 0.01 } },
        };
        var order = await (await kiosk.PostAsJsonAsync("/api/kiosk/orders", tampered)).ReadAsync<KioskOrderDto>();
        Assert.Equal(180m, order.Order.Total);
    }

    [Fact]
    public async Task Cancelling_an_unpaid_order_releases_its_stock()
    {
        var shake = await api.CreateProductAsync("Shake", 80m, stock: 5);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var order = await (await kiosk.OrderAsync(shake.Id, quantity: 3)).ReadAsync<KioskOrderDto>();
        Assert.Equal(2, await api.StockAsync(shake.Id));

        var cancelled = await (await api.Staff(Roles.Cashier).PostJsonAsync(
            $"/api/pos/orders/{order.Order.Id}/cancel", new CancelOrderRequest("Customer left"))).ReadAsync<OrderDto>();

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(5, await api.StockAsync(shake.Id));
    }

    [Fact]
    public async Task Unpaid_orders_expire_after_the_window_and_release_stock()
    {
        var sundae = await api.CreateProductAsync("Sundae", 50m, stock: 4);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var order = await (await kiosk.OrderAsync(sundae.Id, quantity: 4)).ReadAsync<KioskOrderDto>();
        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Order.Id}/pay", new PayRequest(PaymentMethod.Cash))).ReadAsync<KioskOrderDto>();
        Assert.Equal(0, await api.StockAsync(sundae.Id));

        api.Time.Advance(TimeSpan.FromMinutes(16));
        await using (var scope = api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<OrderExpiryService>().ExpireDueAsync(CancellationToken.None);

        var after = await (await kiosk.GetAsync($"/api/kiosk/orders/{order.Order.Id}")).ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.Expired, after.Order.Status);
        Assert.Equal(4, await api.StockAsync(sundae.Id));

        // The cashier can no longer take cash for it.
        (await api.Staff(Roles.Cashier).PostJsonAsync($"/api/pos/orders/{order.Order.Id}/confirm-cash", new ConfirmCashRequest(500m)))
            .AssertStatus(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Order_numbers_are_sequential_and_unique()
    {
        var water = await api.CreateProductAsync("Water", 20m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);

        var orders = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => kiosk.OrderAsync(water.Id)));
        var numbers = await Task.WhenAll(orders.Select(async r => (await r.ReadAsync<KioskOrderDto>()).Order.OrderNumber));

        Assert.Equal(numbers.Length, numbers.Distinct().Count());
    }

    [Fact]
    public async Task Serve_to_table_without_a_table_number_is_a_validation_error()
    {
        var cola = await api.CreateProductAsync("Cola", 40m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        (await kiosk.OrderAsync(cola.Id, type: OrderType.ServeToTable, table: null)).AssertStatus(HttpStatusCode.BadRequest);
        (await kiosk.OrderAsync(cola.Id, type: OrderType.ServeToTable, table: 61)).AssertStatus(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Table_in_use_warning_reflects_active_orders()
    {
        var tea = await api.CreateProductAsync("Tea", 35m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        await (await kiosk.OrderAsync(tea.Id, type: OrderType.ServeToTable, table: 47)).ReadAsync<KioskOrderDto>();

        Assert.True((await (await kiosk.GetAsync("/api/kiosk/tables/47")).ReadAsync<TableStatusDto>()).InUse);
        Assert.False((await (await kiosk.GetAsync("/api/kiosk/tables/48")).ReadAsync<TableStatusDto>()).InUse);
    }
}
