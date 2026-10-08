namespace Kiosk.Domain.Orders;

/// <summary>Append-only status history for one order.</summary>
public class OrderEvent
{
    public long Id { get; set; }
    public Guid OrderId { get; set; }
    public DateTimeOffset At { get; set; }
    public OrderStatus? From { get; set; }
    public OrderStatus To { get; set; }
    public string? ActorId { get; set; }
    public string? Note { get; set; }

    /// <summary>Set when a payment succeeded on an order that could no longer accept it.</summary>
    public bool RefundNeeded { get; set; }
}
