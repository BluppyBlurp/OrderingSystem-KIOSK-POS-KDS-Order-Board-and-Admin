using Kiosk.Application.Orders;
using Kiosk.Application.Receipts;
using Kiosk.Domain.Orders;

namespace Kiosk.Application.Abstractions;

/// <summary>Concurrency-safe daily counter. Must run inside the caller's transaction.</summary>
public interface IOrderNumberGenerator
{
    Task<int> NextAsync(DateOnly businessDate, CancellationToken ct);
}

/// <summary>Pushes realtime updates (SignalR). Call only after the transaction commits.</summary>
public interface IOrderNotifier
{
    Task OrderChangedAsync(OrderDto order, CancellationToken ct = default);
    Task MenuChangedAsync(CancellationToken ct = default);
}

public sealed record CheckoutSession(string ProviderRef, string CheckoutUrl);

/// <summary>
/// A dynamic QR Ph code for one order, scanned straight from any InstaPay bank or e-wallet app.
/// <paramref name="ImageDataUri"/> is ready for an &lt;img src&gt;. <paramref name="TestUrl"/> pays it in test mode only.
/// </summary>
public sealed record QrPhCode(string ProviderRef, string ImageDataUri, string? TestUrl);

/// <summary>
/// A card payment waiting for the reader. <paramref name="PublicKey"/> lets the kiosk turn the card into a gateway
/// payment method itself, so card numbers never reach this API; null on the stub.
/// </summary>
public sealed record CardPaymentStart(string ProviderRef, string? PublicKey);

public enum IntentStatus { Succeeded, Failed, NeedsAction, Pending }

/// <summary>
/// The gateway's own answer about a payment intent, from a call this API made with the secret key, so it can be
/// trusted like a webhook. <paramref name="ActionUrl"/> is the bank's 3DS page for a card.
/// </summary>
public sealed record IntentResult(IntentStatus Status, decimal? Amount, string? FailureReason, string? ActionUrl);

public interface IPaymentGateway
{
    string Name { get; }

    /// <summary>Hosted checkout page (e-wallets).</summary>
    Task<CheckoutSession> CreateCheckoutAsync(Order order, PaymentMethod method, CancellationToken ct);

    Task<QrPhCode> CreateQrPhAsync(Order order, CancellationToken ct);

    Task<CardPaymentStart> CreateCardPaymentAsync(Order order, CancellationToken ct);

    Task<IntentResult> AttachCardAsync(string providerRef, string paymentMethodId, Guid orderId, CancellationToken ct);

    /// <summary>
    /// Current state of a payment intent (QR Ph or card), or null for references this gateway did not create
    /// as intents (hosted checkouts). Backs up the webhook, and stands in for it where none can reach the API.
    /// </summary>
    Task<IntentResult?> GetIntentAsync(string providerRef, CancellationToken ct);
}

/// <summary>Short signed token for the cash slip QR, so a screenshot cannot be replayed against another order.</summary>
public interface ISlipTokenService
{
    string Create(Guid orderId, DateTimeOffset expiresAt);
    Guid? Read(string token);
}

/// <summary>Renders the 80 mm paid receipt and the cash slip as PDF (QuestPDF), for reprints and a future printer.</summary>
public interface IReceiptRenderer
{
    byte[] RenderReceipt(ReceiptModel receipt);
    byte[] RenderSlip(SlipModel slip);
}

/// <summary>
/// Object storage for menu media (Cloudflare R2, S3-compatible). Browsers upload straight to it with a presigned PUT;
/// the API then reads the upload back to validate it and write the public, content-hashed variants.
/// </summary>
public interface IMediaStorage
{
    /// <summary>False until the bucket credentials are configured; media endpoints then answer 503.</summary>
    bool IsConfigured { get; }

    Uri PresignUpload(string key, string contentType, TimeSpan validFor);

    /// <summary>Size in bytes, or null when no object exists under that key.</summary>
    Task<long?> GetSizeAsync(string key, CancellationToken ct);

    Task<byte[]> ReadAsync(string key, CancellationToken ct);

    /// <summary>Stores a public object with a long-lived Cache-Control (keys are content-hashed, so they never change).</summary>
    Task PutPublicAsync(string key, byte[] content, string contentType, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);

    /// <summary>The URL kiosks load the object from.</summary>
    string PublicUrl(string key);
}

/// <summary>WebP variants of an upload, plus the original's real format (sniffed from its bytes, not its name).</summary>
public sealed record ImageVariants(byte[] Display, byte[] Thumbnail, string OriginalExtension, string OriginalContentType);

/// <summary>Decodes an uploaded image and produces WebP display and thumbnail variants.</summary>
public interface IImageProcessor
{
    /// <summary>Throws <see cref="Kiosk.Domain.Common.DomainException"/> when the bytes are not a supported image.</summary>
    ImageVariants CreateVariants(byte[] original);
}

/// <summary>Who is making the current change (staff user id or device id). Used for the audit log.</summary>
public interface ICurrentActor
{
    string? ActorId { get; }
}
