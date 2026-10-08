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

public interface IPaymentGateway
{
    string Name { get; }
    Task<CheckoutSession> CreateCheckoutAsync(Order order, PaymentMethod method, CancellationToken ct);
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
