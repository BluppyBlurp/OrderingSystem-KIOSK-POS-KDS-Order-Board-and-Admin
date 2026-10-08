namespace Kiosk.Domain.Orders;

/// <summary>The single source of truth for legal status transitions (docs §6).</summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Created] = [OrderStatus.AwaitingPayment, OrderStatus.PaymentPending, OrderStatus.Expired, OrderStatus.Cancelled],
        // An earlier online attempt can still succeed after the customer switched to cash; the money is real, so accept it.
        [OrderStatus.AwaitingPayment] = [OrderStatus.Paid, OrderStatus.Expired, OrderStatus.Cancelled],
        [OrderStatus.PaymentPending] = [OrderStatus.Paid, OrderStatus.Failed, OrderStatus.Expired, OrderStatus.Cancelled],
        // Paid is allowed from Failed because webhooks can arrive out of order.
        [OrderStatus.Failed] = [OrderStatus.PaymentPending, OrderStatus.AwaitingPayment, OrderStatus.Paid, OrderStatus.Expired, OrderStatus.Cancelled],
        [OrderStatus.Paid] = [OrderStatus.Preparing],
        [OrderStatus.Preparing] = [OrderStatus.Ready],
        [OrderStatus.Ready] = [OrderStatus.Completed],
        [OrderStatus.Completed] = [],
        [OrderStatus.Expired] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static readonly OrderStatus[] PrePaid =
        [OrderStatus.Created, OrderStatus.AwaitingPayment, OrderStatus.PaymentPending, OrderStatus.Failed];

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed[from].Contains(to);

    public static bool IsPrePaid(OrderStatus status) => PrePaid.Contains(status);
}
