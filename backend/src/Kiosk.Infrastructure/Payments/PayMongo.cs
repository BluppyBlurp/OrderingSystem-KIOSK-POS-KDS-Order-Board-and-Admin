using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Payments;
using Kiosk.Domain.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kiosk.Infrastructure.Payments;

public sealed class PayMongoOptions
{
    public const string Section = "PayMongo";
    public const string ProviderName = "PayMongo";

    public string BaseUrl { get; set; } = "https://api.paymongo.com/v1/";

    /// <summary>sk_test_… for sandbox. Never shipped to a frontend.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Signing secret of the registered webhook (whsk_…).</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Where PayMongo sends the customer afterwards. "{orderId}" is replaced.</summary>
    public string SuccessUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";

    /// <summary>Use an offline stub instead of the real API (local dev and tests only).</summary>
    public bool UseStub { get; set; }
}

/// <summary>Creates a PayMongo Checkout Session (hosted page handles GCash, Maya, card and 3DS).</summary>
internal sealed class PayMongoGateway(HttpClient http, IOptions<PayMongoOptions> options) : IPaymentGateway
{
    public string Name => PayMongoOptions.ProviderName;

    public async Task<CheckoutSession> CreateCheckoutAsync(Order order, PaymentMethod method, CancellationToken ct)
    {
        var o = options.Value;
        // Reported like an outage (502): the kiosk tells the customer to pay at the counter instead.
        if (string.IsNullOrEmpty(o.SecretKey))
            throw new HttpRequestException("PayMongo:SecretKey is not configured; online payment is unavailable.");

        string[] methods = method switch
        {
            PaymentMethod.EWallet => ["gcash", "paymaya"],
            PaymentMethod.Card => ["card"],
            PaymentMethod.QrPh => ["qrph"],
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Cash does not use the gateway."),
        };

        var body = new
        {
            data = new
            {
                attributes = new
                {
                    line_items = order.Items.Select(i => new
                    {
                        currency = "PHP",
                        amount = ToCentavos(i.UnitPriceSnapshot),
                        name = i.NameSnapshot,
                        quantity = i.Quantity,
                    }),
                    payment_method_types = methods,
                    reference_number = order.OrderNumber,
                    description = $"Order {order.OrderNumber}",
                    send_email_receipt = false,
                    show_line_items = true,
                    success_url = o.SuccessUrl.Replace("{orderId}", order.Id.ToString()),
                    cancel_url = o.CancelUrl.Replace("{orderId}", order.Id.ToString()),
                    metadata = new { order_id = order.Id.ToString() },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "checkout_sessions") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(o.SecretKey + ":")));

        using var response = await http.SendAsync(request, ct);
        var json = await response.Content.ReadFromJsonAsync<JsonNode>(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"PayMongo returned {(int)response.StatusCode}: {json?["errors"]?.ToJsonString()}");

        var id = json?["data"]?["id"]?.GetValue<string>();
        var url = json?["data"]?["attributes"]?["checkout_url"]?.GetValue<string>();
        if (id is null || url is null)
            throw new HttpRequestException("PayMongo response did not include a checkout session id and URL.");
        return new CheckoutSession(id, url);
    }

    internal static long ToCentavos(decimal amount) => (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
}

/// <summary>No network. Lets the kiosk flow run locally; complete it by posting a signed webhook.</summary>
internal sealed class StubPaymentGateway(ILogger<StubPaymentGateway> logger) : IPaymentGateway
{
    public string Name => PayMongoOptions.ProviderName;

    public Task<CheckoutSession> CreateCheckoutAsync(Order order, PaymentMethod method, CancellationToken ct)
    {
        var id = $"cs_stub_{Guid.NewGuid():N}";
        logger.LogWarning("Stub payment gateway: checkout {Id} for order {Order}", id, order.OrderNumber);
        return Task.FromResult(new CheckoutSession(id, $"https://checkout.invalid/{id}"));
    }
}

public sealed class InvalidWebhookSignatureException() : Exception("Webhook signature is missing or invalid.");

/// <summary>
/// Verifies the Paymongo-Signature header ("t=…,te=…,li=…") and turns the body into a <see cref="PaymentEvent"/>.
/// Signature = hex(HMAC-SHA256(webhookSecret, "{t}.{rawBody}")), compared against te (test) or li (live).
/// </summary>
public sealed class PayMongoWebhookParser(IOptions<PayMongoOptions> options)
{
    public const string SignatureHeader = "Paymongo-Signature";
    public const string PaidEvent = "checkout_session.payment.paid";

    /// <returns>The event, or null for event types this system does not act on.</returns>
    public PaymentEvent? Parse(string rawBody, string? signatureHeader)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("PayMongo:WebhookSecret is not configured.");

        JsonNode? root;
        try { root = JsonNode.Parse(rawBody); }
        catch (JsonException) { throw new InvalidWebhookSignatureException(); }

        var evt = root?["data"];
        var attrs = evt?["attributes"];
        var live = attrs?["livemode"]?.GetValue<bool>() ?? false;
        if (!SignatureIsValid(rawBody, signatureHeader, secret, live))
            throw new InvalidWebhookSignatureException();

        var eventId = evt?["id"]?.GetValue<string>();
        var type = attrs?["type"]?.GetValue<string>();
        if (eventId is null || type != PaidEvent)
            return null;

        var session = attrs?["data"];
        var sessionId = session?["id"]?.GetValue<string>();
        if (sessionId is null)
            return null;

        long? centavos = null;
        if (session?["attributes"]?["payments"] is JsonArray payments)
        {
            centavos = payments
                .Where(p => p?["attributes"]?["status"]?.GetValue<string>() == "paid")
                .Sum(p => p?["attributes"]?["amount"]?.GetValue<long>() ?? 0);
        }

        return new PaymentEvent(
            PayMongoOptions.ProviderName, eventId, type, Succeeded: true, sessionId,
            centavos is null ? null : centavos.Value / 100m, FailureReason: null);
    }

    public static string Sign(string rawBody, long timestamp, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}")));

    private static bool SignatureIsValid(string rawBody, string? header, string secret, bool live)
    {
        if (string.IsNullOrEmpty(header))
            return false;

        var parts = header.Split(',', StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        if (!parts.TryGetValue("t", out var t) || !long.TryParse(t, out var timestamp))
            return false;
        if (!parts.TryGetValue(live ? "li" : "te", out var given) || string.IsNullOrEmpty(given))
            return false;

        var expected = Sign(rawBody, timestamp, secret);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(given.ToLowerInvariant()));
    }
}
