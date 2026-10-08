using Kiosk.Domain.Common;

namespace Kiosk.Domain.Orders;

public class Order
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Human-facing, daily-resetting number, e.g. "A-101".</summary>
    public string OrderNumber { get; private set; } = "";

    public DateOnly BusinessDate { get; private set; }
    public OrderType Type { get; private set; }

    /// <summary>The physical stand number. Only for <see cref="OrderType.ServeToTable"/>.</summary>
    public int? TableNumber { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>The kiosk that created the order; a kiosk can read only its own orders.</summary>
    public Guid? DeviceId { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal Total { get; private set; }
    public PaymentMethod? PaymentMethod { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public string? CancelReason { get; private set; }

    /// <summary>Optimistic concurrency token (Postgres xmin).</summary>
    public uint Version { get; private set; }

    public List<OrderItem> Items { get; private set; } = [];
    public List<Payment> Payments { get; private set; } = [];
    public List<OrderEvent> Events { get; private set; } = [];

    private Order() { }

    public bool IsPrePaid => OrderStateMachine.IsPrePaid(Status);

    public static Order Create(
        string orderNumber,
        DateOnly businessDate,
        OrderType type,
        int? tableNumber,
        Guid? deviceId,
        IEnumerable<OrderItem> items,
        decimal vatRate,
        DateTimeOffset now,
        TimeSpan paymentWindow)
    {
        if (type == OrderType.ServeToTable && tableNumber is null)
            throw new DomainException("table_number_required", "Serve-to-table orders need a table number.");
        if (type == OrderType.CounterPickup && tableNumber is not null)
            throw new DomainException("table_number_not_allowed", "Counter pickup orders cannot have a table number.");

        var order = new Order
        {
            OrderNumber = orderNumber,
            BusinessDate = businessDate,
            Type = type,
            TableNumber = tableNumber,
            DeviceId = deviceId,
            Status = OrderStatus.Created,
            CreatedAt = now,
            ExpiresAt = now + paymentWindow,
            Items = items.ToList(),
        };
        if (order.Items.Count == 0)
            throw new DomainException("empty_order", "An order needs at least one item.");

        foreach (var item in order.Items)
        {
            if (item.Quantity <= 0)
                throw new DomainException("invalid_quantity", "Quantity must be positive.");
            if (item.UnitPriceSnapshot < 0)
                throw new DomainException("invalid_price", $"{item.NameSnapshot} cannot have a negative price.");
            item.OrderId = order.Id;
            item.LineTotal = Money.Round(item.UnitPriceSnapshot * item.Quantity);
        }

        // Prices are VAT-inclusive (docs §13 #3): the total is the sum of lines; VAT is the portion inside it.
        order.Subtotal = order.Items.Sum(i => i.LineTotal);
        order.Total = order.Subtotal;
        order.TaxAmount = Money.VatPortion(order.Total, vatRate);
        order.Events.Add(new OrderEvent { OrderId = order.Id, At = now, From = null, To = OrderStatus.Created });
        return order;
    }

    /// <summary>Customer picks how to pay. Allowed from Created, and from Failed (retry or switch to cash).</summary>
    public void ChoosePayment(PaymentMethod method, DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Created or OrderStatus.Failed))
            throw new DomainException("payment_already_chosen", $"Cannot choose a payment method while the order is {Status}.");
        EnsureNotExpired(now);

        PaymentMethod = method;
        TransitionTo(method == Orders.PaymentMethod.Cash ? OrderStatus.AwaitingPayment : OrderStatus.PaymentPending, now);
    }

    public Payment StartOnlinePayment(string provider, string providerRef, string checkoutUrl, DateTimeOffset now)
    {
        if (Status != OrderStatus.PaymentPending || PaymentMethod is null or Orders.PaymentMethod.Cash)
            throw new DomainException("not_payment_pending", "Online payment can only start on a PaymentPending order.");

        var payment = new Payment
        {
            OrderId = Id,
            Method = PaymentMethod.Value,
            Provider = provider,
            ProviderRef = providerRef,
            CheckoutUrl = checkoutUrl,
            Status = PaymentStatus.Pending,
            Amount = Total,
            CreatedAt = now,
        };
        Payments.Add(payment);
        return payment;
    }

    /// <summary>Cashier took cash at the counter. Returns the payment with the change due.</summary>
    public Payment ConfirmCash(decimal amountTendered, string cashierId, DateTimeOffset now)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new DomainException("not_awaiting_cash", $"Order is {Status}, not awaiting cash payment.");
        EnsureNotExpired(now);
        if (amountTendered < Total)
            throw new DomainException("insufficient_cash", $"Tendered {amountTendered:0.00} is less than the total {Total:0.00}.");

        var payment = new Payment
        {
            OrderId = Id,
            Method = Orders.PaymentMethod.Cash,
            Provider = "Cash",
            Status = PaymentStatus.Succeeded,
            Amount = Total,
            AmountTendered = amountTendered,
            ChangeDue = Money.Round(amountTendered - Total),
            ProcessedByUserId = cashierId,
            CreatedAt = now,
            CompletedAt = now,
        };
        Payments.Add(payment);
        MarkPaid(now, cashierId, "Cash confirmed at counter");
        return payment;
    }

    /// <summary>
    /// The gateway confirmed payment. Returns false when the order can no longer be paid
    /// (it expired or was cancelled first): the order is not revived, and the event is flagged for a manual refund.
    /// </summary>
    public bool ConfirmOnlinePayment(Payment payment, DateTimeOffset now)
    {
        payment.Status = PaymentStatus.Succeeded;
        payment.CompletedAt = now;

        if (!OrderStateMachine.CanTransition(Status, OrderStatus.Paid))
        {
            Events.Add(new OrderEvent
            {
                OrderId = Id, At = now, From = Status, To = Status, RefundNeeded = true,
                Note = $"{payment.Provider} payment {payment.ProviderRef} succeeded while the order was {Status}",
            });
            return false;
        }

        PaymentMethod = payment.Method;
        MarkPaid(now, null, $"{payment.Provider} payment {payment.ProviderRef} confirmed");
        return true;
    }

    /// <summary>
    /// Customer backed out of the online checkout on the kiosk. The payment stays Pending because they may
    /// still complete it on their phone; if they do, the webhook moves the order from Failed to Paid.
    /// </summary>
    public void AbandonOnlinePayment(DateTimeOffset now)
    {
        if (Status != OrderStatus.PaymentPending)
            throw new DomainException("not_payment_pending", $"Order is {Status}, not in an online checkout.");
        TransitionTo(OrderStatus.Failed, now, note: "Customer cancelled the checkout");
    }

    public void FailOnlinePayment(Payment payment, string? reason, DateTimeOffset now)
    {
        payment.Status = PaymentStatus.Failed;
        payment.CompletedAt = now;
        if (Status == OrderStatus.PaymentPending)
            TransitionTo(OrderStatus.Failed, now, note: reason);
    }

    public void StartPreparing(string actorId, DateTimeOffset now) => TransitionTo(OrderStatus.Preparing, now, actorId);

    public void MarkReady(string actorId, DateTimeOffset now) => TransitionTo(OrderStatus.Ready, now, actorId);

    public void Complete(string actorId, DateTimeOffset now) => TransitionTo(OrderStatus.Completed, now, actorId);

    /// <summary>Expire an unpaid order. The caller must release its reserved stock in the same transaction.</summary>
    public void Expire(DateTimeOffset now)
    {
        if (!IsPrePaid)
            throw new DomainException("not_expirable", $"A {Status} order cannot expire.");
        if (now < ExpiresAt)
            throw new DomainException("not_yet_expired", "The payment window is still open.");
        TransitionTo(OrderStatus.Expired, now);
    }

    /// <summary>Void an unpaid order. The caller must release its reserved stock in the same transaction.</summary>
    public void Cancel(string reason, string actorId, DateTimeOffset now)
    {
        if (!IsPrePaid)
            throw new DomainException("not_cancellable", $"A {Status} order cannot be cancelled.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("reason_required", "A cancellation reason is required.");
        CancelReason = reason.Trim();
        TransitionTo(OrderStatus.Cancelled, now, actorId, CancelReason);
    }

    private void MarkPaid(DateTimeOffset now, string? actorId, string note)
    {
        TransitionTo(OrderStatus.Paid, now, actorId, note);
        PaidAt = now;
    }

    private void EnsureNotExpired(DateTimeOffset now)
    {
        if (now >= ExpiresAt)
            throw new DomainException("order_expired", "The payment window for this order has closed.");
    }

    private void TransitionTo(OrderStatus to, DateTimeOffset now, string? actorId = null, string? note = null)
    {
        if (!OrderStateMachine.CanTransition(Status, to))
            throw new DomainException("illegal_transition", $"Cannot move an order from {Status} to {to}.");
        Events.Add(new OrderEvent { OrderId = Id, At = now, From = Status, To = to, ActorId = actorId, Note = note });
        Status = to;
    }
}
