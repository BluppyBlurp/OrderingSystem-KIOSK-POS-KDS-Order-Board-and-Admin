using System.Net;
using Kiosk.Api.Auth;
using Kiosk.Application.Orders;
using Kiosk.Application.Reports;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace Kiosk.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ReceiptAndReportTests(KioskApiFactory api)
{
    [Fact]
    public async Task Cash_slip_and_receipt_are_pdfs_available_only_in_the_right_state()
    {
        var burger = await api.CreateProductAsync("Receipt Burger", 120m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var otherKiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var cashier = api.Staff(Roles.Cashier);

        var created = await (await kiosk.OrderAsync(burger.Id, quantity: 2)).ReadAsync<KioskOrderDto>();
        var id = created.Order.Id;

        // Before choosing cash there is no slip, and never a receipt before payment.
        (await kiosk.GetAsync($"/api/kiosk/orders/{id}/slip")).AssertStatus(HttpStatusCode.Conflict);
        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{id}/pay", new PayRequest(PaymentMethod.Cash))).ReadAsync<KioskOrderDto>();
        (await cashier.GetAsync($"/api/pos/orders/{id}/receipt")).AssertStatus(HttpStatusCode.Conflict);

        var slip = await kiosk.GetAsync($"/api/kiosk/orders/{id}/slip");
        await AssertPdfAsync(slip, $"slip-{created.Order.OrderNumber}.pdf");
        (await otherKiosk.GetAsync($"/api/kiosk/orders/{id}/slip")).AssertStatus(HttpStatusCode.NotFound);

        await (await cashier.PostJsonAsync($"/api/pos/orders/{id}/confirm-cash", new ConfirmCashRequest(300m))).ReadAsync<CashConfirmationDto>();

        var receipt = await cashier.GetAsync($"/api/pos/orders/{id}/receipt");
        await AssertPdfAsync(receipt, $"receipt-{created.Order.OrderNumber}.pdf");
        (await api.Staff(Roles.Kitchen).GetAsync($"/api/pos/orders/{id}/receipt")).AssertStatus(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Shift_summary_counts_only_this_cashiers_cash()
    {
        var soda = await api.CreateProductAsync("Shift Soda", 45m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var cashierId = $"shift_{Guid.NewGuid():N}";
        var me = api.Staff(Roles.Cashier, cashierId);
        var colleague = api.Staff(Roles.Cashier, $"other_{Guid.NewGuid():N}");

        async Task<Guid> CashOrderAsync(int quantity)
        {
            var o = await (await kiosk.OrderAsync(soda.Id, quantity)).ReadAsync<KioskOrderDto>();
            await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{o.Order.Id}/pay", new PayRequest(PaymentMethod.Cash))).ReadAsync<KioskOrderDto>();
            return o.Order.Id;
        }

        await (await me.PostJsonAsync($"/api/pos/orders/{await CashOrderAsync(2)}/confirm-cash", new ConfirmCashRequest(100m))).ReadAsync<CashConfirmationDto>();
        await (await me.PostJsonAsync($"/api/pos/orders/{await CashOrderAsync(1)}/confirm-cash", new ConfirmCashRequest(50m))).ReadAsync<CashConfirmationDto>();
        await (await colleague.PostJsonAsync($"/api/pos/orders/{await CashOrderAsync(3)}/confirm-cash", new ConfirmCashRequest(200m))).ReadAsync<CashConfirmationDto>();
        await (await me.PostJsonAsync($"/api/pos/orders/{await CashOrderAsync(1)}/cancel", new CancelOrderRequest("Customer left"))).ReadAsync<OrderDto>();

        var summary = await (await me.GetAsync("/api/pos/shift-summary")).ReadAsync<ShiftSummaryDto>();
        Assert.Equal(2, summary.Mine.Orders);
        Assert.Equal(135m, summary.Mine.CashCollected); // the order totals, not what was handed over
        Assert.True(summary.AllCashiers.CashCollected >= 270m);
        Assert.Equal(1, summary.CancelledByMe);

        api.Time.Advance(TimeSpan.FromSeconds(1)); // a new shift starting now has taken nothing yet
        var later = Uri.EscapeDataString(api.Time.GetUtcNow().ToString("O"));
        var empty = await (await me.GetAsync($"/api/pos/shift-summary?since={later}")).ReadAsync<ShiftSummaryDto>();
        Assert.Equal(0, empty.Mine.Orders);
    }

    [Fact]
    public async Task Sales_report_counts_paid_orders_only()
    {
        var admin = api.Staff(Roles.Manager);
        var today = DateOnly.FromDateTime(api.Time.GetUtcNow().UtcDateTime);
        var query = $"/api/admin/reports/sales?from={today.AddDays(-1):yyyy-MM-dd}&to={today.AddDays(1):yyyy-MM-dd}";
        var before = await (await admin.GetAsync(query)).ReadAsync<SalesReportDto>();

        var pie = await api.CreateProductAsync($"Report Pie {Guid.NewGuid():N}", 112m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var paid = await (await kiosk.OrderAsync(pie.Id, quantity: 3)).ReadAsync<KioskOrderDto>();
        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{paid.Order.Id}/pay", new PayRequest(PaymentMethod.Cash))).ReadAsync<KioskOrderDto>();
        await (await api.Staff(Roles.Cashier).PostJsonAsync($"/api/pos/orders/{paid.Order.Id}/confirm-cash", new ConfirmCashRequest(400m)))
            .ReadAsync<CashConfirmationDto>();
        await kiosk.OrderAsync(pie.Id); // created but never paid: not a sale

        var after = await (await admin.GetAsync(query)).ReadAsync<SalesReportDto>();
        Assert.Equal(before.Orders + 1, after.Orders);
        Assert.Equal(before.GrossSales + 336m, after.GrossSales);
        Assert.Equal(before.VatAmount + 36m, after.VatAmount);
        Assert.Equal(after.GrossSales - after.VatAmount, after.NetOfVat);
        var line = Assert.Single(after.TopProducts, p => p.ProductId == pie.Id);
        Assert.Equal(3, line.Quantity);
        Assert.Contains(after.ByPaymentMethod, m => m.Method == PaymentMethod.Cash);

        (await admin.GetAsync($"/api/admin/reports/sales?from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}"))
            .AssertStatus(HttpStatusCode.Conflict);
        (await api.Staff(Roles.Cashier).GetAsync(query)).AssertStatus(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Payment_on_an_expired_order_shows_up_as_a_refund_to_make()
    {
        var meal = await api.CreateProductAsync("Late Meal", 150m, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var created = await (await kiosk.OrderAsync(meal.Id)).ReadAsync<KioskOrderDto>();
        var checkout = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{created.Order.Id}/pay", new PayRequest(PaymentMethod.EWallet)))
            .ReadAsync<KioskOrderDto>();

        api.Time.Advance(TimeSpan.FromMinutes(16));
        await using (var scope = api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<OrderExpiryService>().ExpireDueAsync(CancellationToken.None);

        var sessionId = checkout.CheckoutUrl!.Split('/').Last();
        (await api.PayMongoPaidAsync($"evt_{Guid.NewGuid():N}", sessionId, 150m)).EnsureSuccessStatusCode();

        var refunds = await (await api.Staff(Roles.Admin).GetAsync("/api/admin/reports/refunds-needed")).ReadAsync<List<RefundNeededDto>>();
        var refund = Assert.Single(refunds, r => r.OrderId == created.Order.Id);
        Assert.Equal(OrderStatus.Expired, refund.OrderStatus);
        Assert.Equal(150m, refund.OrderTotal);
    }

    private static async Task AssertPdfAsync(HttpResponseMessage response, string fileName)
    {
        response.AssertStatus(HttpStatusCode.OK);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
