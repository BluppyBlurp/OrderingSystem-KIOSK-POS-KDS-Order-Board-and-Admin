using System.Net;
using Kiosk.Application.Orders;
using Kiosk.Application.Payments;
using Kiosk.Domain.Devices;
using Kiosk.Domain.Orders;
using Kiosk.Infrastructure.Payments;
using Kiosk.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kiosk.IntegrationTests;

/// <summary>QR Ph and card tap: PayMongo Payment Intents, run against the stub gateway.</summary>
[Collection(ApiCollection.Name)]
public sealed class PaymentIntentFlowTests(KioskApiFactory api)
{
    [Fact]
    public async Task QrPh_order_shows_a_QR_code_and_is_paid_by_the_payment_webhook()
    {
        var (kiosk, order) = await PlaceAsync(149m);

        var pay = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.QrPh)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.PaymentPending, pay.Order.Status);
        Assert.StartsWith("data:image/", pay.QrImage);
        Assert.Null(pay.CheckoutUrl);

        var intentId = await IntentIdAsync(order.Id);
        var paid = await (await api.PayMongoPaymentAsync($"evt_{Guid.NewGuid():N}", PayMongoWebhookParser.PaymentPaidEvent, intentId, 149m))
            .ReadAsync<Dictionary<string, string>>();
        Assert.Equal(nameof(WebhookOutcome.Applied), paid["outcome"]);

        var polled = await (await kiosk.GetAsync($"/api/kiosk/orders/{order.Id}")).ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.Paid, polled.Order.Status);
        Assert.Equal(PaymentMethod.QrPh, polled.Order.PaymentMethod);
    }

    [Fact]
    public async Task A_failed_QrPh_payment_lets_the_customer_pay_at_the_counter()
    {
        var (kiosk, order) = await PlaceAsync(99m);
        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.QrPh))).ReadAsync<KioskOrderDto>();

        await (await api.PayMongoPaymentAsync($"evt_{Guid.NewGuid():N}", PayMongoWebhookParser.PaymentFailedEvent, await IntentIdAsync(order.Id), 99m))
            .ReadAsync<Dictionary<string, string>>();
        var failed = await (await kiosk.GetAsync($"/api/kiosk/orders/{order.Id}")).ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.Failed, failed.Order.Status);

        var cash = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.Cash)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.AwaitingPayment, cash.Order.Status);
    }

    [Fact]
    public async Task Card_tap_settles_the_order_at_once_and_the_later_webhook_is_a_no_op()
    {
        var (kiosk, order) = await PlaceAsync(189m);
        var start = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.Card)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.PaymentPending, start.Order.Status);
        Assert.Null(start.CardPublicKey); // stub: the simulated reader sends stub payment methods

        var tapped = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/card-tap", new CardTapRequest(StubPaymentGateway.Success)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.Paid, tapped.Order.Status);
        Assert.Equal(PaymentMethod.Card, tapped.Order.PaymentMethod);

        var late = await (await api.PayMongoPaymentAsync($"evt_{Guid.NewGuid():N}", PayMongoWebhookParser.PaymentPaidEvent, await IntentIdAsync(order.Id), 189m))
            .ReadAsync<Dictionary<string, string>>();
        Assert.Equal(nameof(WebhookOutcome.Ignored), late["outcome"]);
    }

    [Theory]
    [InlineData(StubPaymentGateway.Declined)]
    [InlineData(StubPaymentGateway.InsufficientFunds)]
    [InlineData(StubPaymentGateway.Expired)]
    public async Task A_refused_card_fails_the_order_and_the_customer_can_try_again(string card)
    {
        var (kiosk, order) = await PlaceAsync(129m);
        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.Card))).ReadAsync<KioskOrderDto>();

        var refused = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/card-tap", new CardTapRequest(card)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.Failed, refused.Order.Status);

        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.Card))).ReadAsync<KioskOrderDto>();
        var retried = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/card-tap", new CardTapRequest(StubPaymentGateway.Success)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.Paid, retried.Order.Status);
    }

    [Fact]
    public async Task A_card_that_needs_3DS_hands_the_bank_page_to_the_kiosk_and_stays_pending()
    {
        var (kiosk, order) = await PlaceAsync(159m);
        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.Card))).ReadAsync<KioskOrderDto>();

        var challenge = await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/card-tap", new CardTapRequest(StubPaymentGateway.ThreeDSecure)))
            .ReadAsync<KioskOrderDto>();
        Assert.Equal(OrderStatus.PaymentPending, challenge.Order.Status);
        Assert.StartsWith("https://3ds.invalid/", challenge.CheckoutUrl);

        var polled = await (await kiosk.GetAsync($"/api/kiosk/orders/{order.Id}")).ReadAsync<KioskOrderDto>();
        Assert.Equal(challenge.CheckoutUrl, polled.CheckoutUrl);
    }

    [Theory]
    [InlineData("4343434343434345")] // a card number, which must never reach this API
    [InlineData("")]
    [InlineData("pm_ x")]
    public async Task Card_tap_accepts_only_a_payment_method_id(string value)
    {
        var (kiosk, order) = await PlaceAsync(45m);
        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.Card))).ReadAsync<KioskOrderDto>();

        (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/card-tap", new CardTapRequest(value)))
            .AssertStatus(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Card_tap_is_refused_on_an_order_not_waiting_for_a_card()
    {
        var (kiosk, order) = await PlaceAsync(45m);
        await (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/pay", new PayRequest(PaymentMethod.QrPh))).ReadAsync<KioskOrderDto>();

        (await kiosk.PostJsonAsync($"/api/kiosk/orders/{order.Id}/card-tap", new CardTapRequest(StubPaymentGateway.Success)))
            .AssertStatus(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Another_kiosk_cannot_tap_a_card_on_this_kiosks_order()
    {
        var (_, order) = await PlaceAsync(45m);
        var other = await api.DeviceAsync(DeviceKind.Kiosk);

        (await other.PostJsonAsync($"/api/kiosk/orders/{order.Id}/card-tap", new CardTapRequest(StubPaymentGateway.Success)))
            .AssertStatus(HttpStatusCode.NotFound);
    }

    private async Task<(HttpClient Kiosk, OrderDto Order)> PlaceAsync(decimal price)
    {
        var product = await api.CreateProductAsync($"Item {Guid.NewGuid():N}", price, stock: null);
        var kiosk = await api.DeviceAsync(DeviceKind.Kiosk);
        var created = await (await kiosk.OrderAsync(product.Id)).ReadAsync<KioskOrderDto>();
        return (kiosk, created.Order);
    }

    private async Task<string> IntentIdAsync(Guid orderId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Payments
            .Where(p => p.OrderId == orderId && p.ProviderRef != null)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.ProviderRef!)
            .FirstAsync(TestContext.Current.CancellationToken);
    }
}
