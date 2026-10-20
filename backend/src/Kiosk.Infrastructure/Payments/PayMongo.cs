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

    /// <summary>pk_test_… / pk_live_…. Safe in a browser: the kiosk's card reader uses it to turn a card into a payment method.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Signing secret of the registered webhook (whsk_…).</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Where PayMongo sends the customer afterwards (hosted checkout, card 3DS). "{orderId}" is replaced.</summary>
    public string SuccessUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";

    /// <summary>Use an offline stub instead of the real API (local dev and tests only).</summary>
    public bool UseStub { get; set; }
}

/// <summary>
/// PayMongo. E-wallets use a hosted Checkout Session. QR Ph and card use Payment Intents: QR Ph so the kiosk can show
/// the real QR Ph code (any InstaPay bank or e-wallet app scans it), card so a reader at the kiosk can pay it.
/// </summary>
internal sealed class PayMongoGateway(HttpClient http, IOptions<PayMongoOptions> options, TimeProvider time) : IPaymentGateway
{
    // PayMongo allows a QR Ph code to live 60–9000 s.
    private static readonly TimeSpan QrMinLife = TimeSpan.FromSeconds(60), QrMaxLife = TimeSpan.FromSeconds(9000);

    public string Name => PayMongoOptions.ProviderName;

    public async Task<CheckoutSession> CreateCheckoutAsync(Order order, PaymentMethod method, CancellationToken ct)
    {
        var o = options.Value;
        string[] methods = method switch
        {
            PaymentMethod.EWallet => ["gcash", "paymaya"],
            PaymentMethod.Card => ["card"],
            PaymentMethod.QrPh => ["qrph"],
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Cash does not use the gateway."),
        };

        var json = await SendAsync(HttpMethod.Post, "checkout_sessions", new
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
                    success_url = UrlFor(o.SuccessUrl, order.Id),
                    cancel_url = UrlFor(o.CancelUrl, order.Id),
                    metadata = new { order_id = order.Id.ToString() },
                },
            },
        }, ct);

        var id = json["data"]?["id"]?.GetValue<string>();
        var url = json["data"]?["attributes"]?["checkout_url"]?.GetValue<string>();
        if (id is null || url is null)
            throw new HttpRequestException("PayMongo response did not include a checkout session id and URL.");
        return new CheckoutSession(id, url);
    }

    public async Task<QrPhCode> CreateQrPhAsync(Order order, CancellationToken ct)
    {
        var intentId = await CreateIntentAsync(order, "qrph", methodOptions: null, ct);

        var life = order.ExpiresAt - time.GetUtcNow();
        life = life < QrMinLife ? QrMinLife : life > QrMaxLife ? QrMaxLife : life;
        var method = await SendAsync(HttpMethod.Post, "payment_methods", new
        {
            data = new { attributes = new { type = "qrph", expiry_seconds = (int)life.TotalSeconds } },
        }, ct);
        var methodId = method["data"]?["id"]?.GetValue<string>()
            ?? throw new HttpRequestException("PayMongo response did not include a QR Ph payment method id.");

        var attached = await SendAsync(HttpMethod.Post, $"payment_intents/{intentId}/attach", new
        {
            data = new { attributes = new { payment_method = methodId } },
        }, ct);

        var code = attached["data"]?["attributes"]?["next_action"]?["code"];
        var image = code?["image_url"]?.GetValue<string>()
            ?? throw new HttpRequestException("PayMongo response did not include a QR Ph image.");
        return new QrPhCode(intentId, AsImageSource(image), code?["test_url"]?.GetValue<string>());
    }

    public async Task<CardPaymentStart> CreateCardPaymentAsync(Order order, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.Value.PublicKey))
            throw new HttpRequestException("PayMongo:PublicKey is not configured; the card reader cannot pay.");

        var intentId = await CreateIntentAsync(
            order, "card", new { card = new { request_three_d_secure = "automatic" } }, ct);
        return new CardPaymentStart(intentId, options.Value.PublicKey);
    }

    public async Task<IntentResult> AttachCardAsync(string providerRef, string paymentMethodId, Guid orderId, CancellationToken ct)
    {
        using var response = await SendRawAsync(HttpMethod.Post, $"payment_intents/{providerRef}/attach", new
        {
            data = new
            {
                attributes = new
                {
                    payment_method = paymentMethodId,
                    return_url = UrlFor(options.Value.SuccessUrl, orderId), // where the bank's 3DS page ends
                },
            },
        }, ct);
        var json = await ReadAsync(response, ct);

        // A card the bank refuses comes back as a 4xx with errors[], not as a failed intent.
        if ((int)response.StatusCode is >= 400 and < 500)
            return new IntentResult(IntentStatus.Failed, null, ErrorDetail(json) ?? "The card was declined.", null);
        EnsureSuccess(response, json);
        return ParseIntent(json["data"]);
    }

    public async Task<IntentResult?> GetIntentAsync(string providerRef, CancellationToken ct)
    {
        if (!providerRef.StartsWith("pi_", StringComparison.Ordinal))
            return null; // a hosted checkout session: only its webhook reports it
        return ParseIntent((await SendAsync(HttpMethod.Get, $"payment_intents/{providerRef}", null, ct))["data"]);
    }

    private async Task<string> CreateIntentAsync(Order order, string methodType, object? methodOptions, CancellationToken ct)
    {
        var json = await SendAsync(HttpMethod.Post, "payment_intents", new
        {
            data = new
            {
                attributes = new
                {
                    amount = ToCentavos(order.Total),
                    currency = "PHP",
                    payment_method_allowed = new[] { methodType },
                    payment_method_options = methodOptions,
                    description = $"Order {order.OrderNumber}",
                    metadata = new { order_id = order.Id.ToString(), order_number = order.OrderNumber },
                },
            },
        }, ct);
        return json["data"]?["id"]?.GetValue<string>()
            ?? throw new HttpRequestException("PayMongo response did not include a payment intent id.");
    }

    /// <summary>
    /// Maps a PaymentIntent. A failed attempt leaves it back in awaiting_payment_method with last_payment_error set;
    /// awaiting_payment_method without an error just means nothing was tried yet.
    /// </summary>
    internal static IntentResult ParseIntent(JsonNode? intent)
    {
        var attrs = intent?["attributes"];
        var status = attrs?["status"]?.GetValue<string>();
        var error = attrs?["last_payment_error"];
        decimal? amount = attrs?["amount"]?.GetValue<long>() is { } centavos ? centavos / 100m : null;

        return status switch
        {
            "succeeded" => new IntentResult(IntentStatus.Succeeded, amount, null, null),
            "awaiting_next_action" => new IntentResult(
                IntentStatus.NeedsAction, amount, null, attrs?["next_action"]?["redirect"]?["url"]?.GetValue<string>()),
            "awaiting_payment_method" when error is JsonObject => new IntentResult(
                IntentStatus.Failed, amount, Text(error, "failed_message", "message", "detail", "failed_code", "code"), null),
            "cancelled" => new IntentResult(IntentStatus.Failed, amount, "The payment was cancelled.", null),
            _ => new IntentResult(IntentStatus.Pending, amount, null, null),
        };
    }

    /// <summary>The docs describe image_url as a Base64 PNG; accept it bare, as a data URI, or as a URL.</summary>
    internal static string AsImageSource(string image) =>
        image.StartsWith("data:", StringComparison.Ordinal) || image.StartsWith("https://", StringComparison.Ordinal)
            ? image
            : $"data:image/png;base64,{image}";

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, ct);
        var json = await ReadAsync(response, ct);
        EnsureSuccess(response, json);
        return json;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        // Reported like an outage (502): the kiosk tells the customer to pay at the counter instead.
        var key = options.Value.SecretKey;
        if (string.IsNullOrEmpty(key))
            throw new HttpRequestException("PayMongo:SecretKey is not configured; online payment is unavailable.");

        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: SkipNulls);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(key + ":")));
        return await http.SendAsync(request, ct);
    }

    private static readonly JsonSerializerOptions SkipNulls = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadFromJsonAsync<JsonNode>(ct) ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    private static void EnsureSuccess(HttpResponseMessage response, JsonNode json)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"PayMongo returned {(int)response.StatusCode}: {json["errors"]?.ToJsonString()}");
    }

    private static string? ErrorDetail(JsonNode json) =>
        json["errors"] is JsonArray { Count: > 0 } errors ? Text(errors[0], "detail", "code") : null;

    private static string? Text(JsonNode? node, params string[] fields) =>
        fields.Select(f => node?[f] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private static string UrlFor(string template, Guid orderId) => template.Replace("{orderId}", orderId.ToString());

    internal static long ToCentavos(decimal amount) => (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
}

/// <summary>
/// No network. Lets the kiosk flow run locally. QR Ph and hosted checkouts are completed with the dev
/// simulate-payment endpoint; a card is settled by which stub payment method the simulated reader sends.
/// </summary>
internal sealed class StubPaymentGateway(ILogger<StubPaymentGateway> logger) : IPaymentGateway
{
    /// <summary>The simulated reader's stand-ins for PayMongo's test cards: pm_stub_{outcome}.</summary>
    public const string Success = "pm_stub_success", Declined = "pm_stub_declined",
        InsufficientFunds = "pm_stub_insufficient_funds", Expired = "pm_stub_expired", ThreeDSecure = "pm_stub_3ds";

    // A placeholder, not a scannable code: there is nothing to pay on the stub.
    private const string PlaceholderQr =
        "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>" +
        "<rect width='200' height='200' fill='white' stroke='black' stroke-width='6'/>" +
        "<text x='100' y='92' font-family='sans-serif' font-size='22' text-anchor='middle'>TEST QR Ph</text>" +
        "<text x='100' y='122' font-family='sans-serif' font-size='13' text-anchor='middle'>use Simulate payment</text></svg>";

    public string Name => PayMongoOptions.ProviderName;

    public Task<CheckoutSession> CreateCheckoutAsync(Order order, PaymentMethod method, CancellationToken ct)
    {
        var id = $"cs_stub_{Guid.NewGuid():N}";
        logger.LogWarning("Stub payment gateway: checkout {Id} for order {Order}", id, order.OrderNumber);
        return Task.FromResult(new CheckoutSession(id, $"https://checkout.invalid/{id}"));
    }

    public Task<QrPhCode> CreateQrPhAsync(Order order, CancellationToken ct)
    {
        var id = $"pi_stub_{Guid.NewGuid():N}";
        logger.LogWarning("Stub payment gateway: QR Ph {Id} for order {Order}", id, order.OrderNumber);
        return Task.FromResult(new QrPhCode(id, PlaceholderQr, TestUrl: null));
    }

    public Task<CardPaymentStart> CreateCardPaymentAsync(Order order, CancellationToken ct)
    {
        var id = $"pi_stub_{Guid.NewGuid():N}";
        logger.LogWarning("Stub payment gateway: card payment {Id} for order {Order}", id, order.OrderNumber);
        return Task.FromResult(new CardPaymentStart(id, PublicKey: null));
    }

    public Task<IntentResult> AttachCardAsync(string providerRef, string paymentMethodId, Guid orderId, CancellationToken ct) =>
        Task.FromResult(paymentMethodId switch
        {
            Success => new IntentResult(IntentStatus.Succeeded, null, null, null),
            Declined => new IntentResult(IntentStatus.Failed, null, "The card was declined.", null),
            InsufficientFunds => new IntentResult(IntentStatus.Failed, null, "Insufficient funds.", null),
            Expired => new IntentResult(IntentStatus.Failed, null, "The card has expired.", null),
            ThreeDSecure => new IntentResult(IntentStatus.NeedsAction, null, null, $"https://3ds.invalid/{providerRef}"),
            _ => new IntentResult(IntentStatus.Failed, null, "Unknown test card.", null),
        });

    public Task<IntentResult?> GetIntentAsync(string providerRef, CancellationToken ct) => Task.FromResult<IntentResult?>(null);
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

    /// <summary>Payment Intent payments (QR Ph, card): the payment resource carries its payment_intent_id.</summary>
    public const string PaymentPaidEvent = "payment.paid", PaymentFailedEvent = "payment.failed";

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
        if (eventId is null)
            return null;

        return type switch
        {
            PaidEvent => ParseCheckoutPaid(eventId, type, attrs?["data"]),
            PaymentPaidEvent or PaymentFailedEvent => ParsePayment(eventId, type, attrs?["data"]),
            _ => null,
        };
    }

    private static PaymentEvent? ParseCheckoutPaid(string eventId, string type, JsonNode? session)
    {
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

    private static PaymentEvent? ParsePayment(string eventId, string type, JsonNode? payment)
    {
        var attrs = payment?["attributes"];
        // Hosted checkouts also raise payment.paid; those are matched by their checkout_session event instead.
        var intentId = attrs?["payment_intent_id"]?.GetValue<string>();
        if (intentId is null)
            return null;

        var paid = type == PaymentPaidEvent;
        return new PaymentEvent(
            PayMongoOptions.ProviderName, eventId, type, paid, intentId,
            attrs?["amount"]?.GetValue<long>() is { } centavos ? centavos / 100m : null,
            paid ? null : attrs?["failed_message"]?.GetValue<string>() ?? attrs?["failed_code"]?.GetValue<string>());
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
