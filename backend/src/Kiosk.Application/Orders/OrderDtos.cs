using Kiosk.Domain.Orders;

namespace Kiosk.Application.Orders;

public sealed record OrderItemModifierDto(string Name, decimal PriceDelta);

public sealed record OrderItemDto(
    Guid ProductId,
    string Name,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string? Notes,
    IReadOnlyList<OrderItemModifierDto> Modifiers);

public sealed record OrderDto(
    Guid Id,
    string OrderNumber,
    OrderType Type,
    int? TableNumber,
    OrderStatus Status,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    PaymentMethod? PaymentMethod,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? PaidAt,
    IReadOnlyList<OrderItemDto> Items)
{
    public static OrderDto From(Order o) => new(
        o.Id, o.OrderNumber, o.Type, o.TableNumber, o.Status,
        o.Subtotal, o.TaxAmount, o.Total, o.PaymentMethod,
        o.CreatedAt, o.ExpiresAt, o.PaidAt,
        o.Items.Select(i => new OrderItemDto(
            i.ProductId, i.NameSnapshot, i.Quantity, i.UnitPriceSnapshot, i.LineTotal, i.Notes,
            i.Modifiers.Select(m => new OrderItemModifierDto(m.NameSnapshot, m.PriceDeltaSnapshot)).ToList())).ToList());
}

/// <summary>What the kiosk needs to show after paying: the cash slip QR token or the checkout URL.</summary>
public sealed record KioskOrderDto(OrderDto Order, string? SlipToken, string? CheckoutUrl);

public sealed record CreateOrderLine(Guid ProductId, int Quantity, IReadOnlyList<Guid>? ModifierIds, string? Notes);

/// <summary>The kiosk never sends prices or totals; the server computes them.</summary>
public sealed record CreateOrderRequest(OrderType OrderType, int? TableNumber, IReadOnlyList<CreateOrderLine> Items);

public sealed record PayRequest(PaymentMethod Method);

public sealed record TableStatusDto(int TableNumber, bool InUse);
