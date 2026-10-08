namespace Kiosk.Domain.Orders;

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public PaymentMethod Method { get; set; }

    /// <summary>"Cash" or the gateway name, e.g. "PayMongo".</summary>
    public required string Provider { get; set; }

    /// <summary>Gateway reference, e.g. the PayMongo checkout session id.</summary>
    public string? ProviderRef { get; set; }

    public string? CheckoutUrl { get; set; }
    public PaymentStatus Status { get; set; }
    public decimal Amount { get; set; }
    public decimal? AmountTendered { get; set; }
    public decimal? ChangeDue { get; set; }
    public string? ProcessedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
