using System.Net;
using System.Net.Http.Json;
using Kiosk.Api.Auth;
using Kiosk.Application.Admin;
using Kiosk.Application.Orders;
using Kiosk.Domain.Orders;
using Kiosk.Infrastructure.Payments;

namespace Kiosk.IntegrationTests;

/// <summary>Small helpers so each test reads as the business flow it checks.</summary>
internal static class TestApi
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            Assert.Fail($"{(int)response.StatusCode} {response.ReasonPhrase}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(KioskApiFactory.Json))!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, KioskApiFactory.Json);

    public static async Task<AdminProductDto> CreateProductAsync(this KioskApiFactory api, string name, decimal price, int? stock)
    {
        var admin = api.Staff(Roles.Manager);
        var category = await (await admin.PostJsonAsync("/api/admin/categories",
            new UpsertCategoryRequest($"Cat {Guid.NewGuid():N}", 0, true))).ReadAsync<CategoryDto>();
        return await (await admin.PostJsonAsync("/api/admin/products",
            new UpsertProductRequest(category.Id, name, null, price, stock, true, 0, null))).ReadAsync<AdminProductDto>();
    }

    public static Task<HttpResponseMessage> OrderAsync(this HttpClient kiosk, Guid productId, int quantity = 1,
        OrderType type = OrderType.CounterPickup, int? table = null) =>
        kiosk.PostJsonAsync("/api/kiosk/orders",
            new CreateOrderRequest(type, table, [new CreateOrderLine(productId, quantity, null, null)]));

    public static async Task<int?> StockAsync(this KioskApiFactory api, Guid productId) =>
        (await (await api.Staff(Roles.Admin).GetAsync($"/api/admin/products/{productId}")).ReadAsync<AdminProductDto>()).Stock;

    /// <summary>Posts a PayMongo "checkout_session.payment.paid" event signed with the test webhook secret.</summary>
    public static Task<HttpResponseMessage> PayMongoPaidAsync(this KioskApiFactory api, string eventId, string sessionId, decimal amount)
    {
        var centavos = (long)(amount * 100);
        var body = """
            {"data":{"id":"EVENT_ID","type":"event","attributes":{"type":"checkout_session.payment.paid","livemode":false,"data":{"id":"SESSION_ID","type":"checkout_session","attributes":{"payments":[{"id":"pay_x","attributes":{"amount":CENTAVOS,"status":"paid"}}]}}}}}
            """
            .Replace("EVENT_ID", eventId).Replace("SESSION_ID", sessionId).Replace("CENTAVOS", centavos.ToString());
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/paymongo")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(PayMongoWebhookParser.SignatureHeader,
            $"t={timestamp},te={PayMongoWebhookParser.Sign(body, timestamp, KioskApiFactory.WebhookSecret)},li=");
        return api.CreateClient().SendAsync(request);
    }

    public static void AssertStatus(this HttpResponseMessage response, HttpStatusCode expected) =>
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}: {response.Content.ReadAsStringAsync().Result}");
}
