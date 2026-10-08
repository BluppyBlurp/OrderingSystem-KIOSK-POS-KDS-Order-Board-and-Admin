namespace Kiosk.Domain.Orders;

/// <summary>Idempotency record: a provider event id is processed at most once.</summary>
public class ProcessedWebhookEvent
{
    public required string Id { get; set; }
    public required string Provider { get; set; }
    public required string EventType { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
