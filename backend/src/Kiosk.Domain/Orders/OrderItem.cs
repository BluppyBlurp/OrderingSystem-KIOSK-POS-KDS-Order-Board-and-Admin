namespace Kiosk.Domain.Orders;

/// <summary>Name and prices are snapshots: later menu edits never change a past order.</summary>
public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public required string NameSnapshot { get; set; }

    /// <summary>Base price plus all modifier deltas, for one unit.</summary>
    public decimal UnitPriceSnapshot { get; set; }

    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
    public List<OrderItemModifier> Modifiers { get; set; } = [];
}

public class OrderItemModifier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderItemId { get; set; }
    public Guid ModifierId { get; set; }
    public required string NameSnapshot { get; set; }
    public decimal PriceDeltaSnapshot { get; set; }
}
