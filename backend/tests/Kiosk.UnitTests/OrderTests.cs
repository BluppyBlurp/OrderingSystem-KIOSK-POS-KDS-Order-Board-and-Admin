using Kiosk.Domain.Common;
using Kiosk.Domain.Orders;

namespace Kiosk.UnitTests;

public class OrderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 4, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static Order NewOrder(OrderType type = OrderType.CounterPickup, int? table = null, decimal unitPrice = 142.50m, int qty = 2) =>
        Order.Create("A-101", new DateOnly(2026, 10, 8), type, table, Guid.NewGuid(),
            [new OrderItem { ProductId = Guid.NewGuid(), NameSnapshot = "Burger", UnitPriceSnapshot = unitPrice, Quantity = qty }],
            vatRate: 0.12m, T0, Window);

    [Fact]
    public void Create_computes_vat_inclusive_totals_from_snapshots()
    {
        var order = NewOrder(unitPrice: 142.50m, qty: 2);

        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal(285.00m, order.Items[0].LineTotal);
        Assert.Equal(285.00m, order.Subtotal);
        Assert.Equal(285.00m, order.Total);
        Assert.Equal(30.54m, order.TaxAmount); // 285 × 12/112
        Assert.Equal(T0 + Window, order.ExpiresAt);
    }

    [Fact]
    public void Serve_to_table_requires_a_table_number()
    {
        var ex = Assert.Throws<DomainException>(() => NewOrder(OrderType.ServeToTable, table: null));
        Assert.Equal("table_number_required", ex.Code);
    }

    [Fact]
    public void Counter_pickup_rejects_a_table_number()
    {
        var ex = Assert.Throws<DomainException>(() => NewOrder(OrderType.CounterPickup, table: 12));
        Assert.Equal("table_number_not_allowed", ex.Code);
    }

    [Fact]
    public void Negative_unit_price_is_rejected()
    {
        var ex = Assert.Throws<DomainException>(() => NewOrder(unitPrice: -1m));
        Assert.Equal("invalid_price", ex.Code);
    }

    [Fact]
    public void Cash_happy_path_reaches_completed_and_records_history()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.Cash, T0.AddMinutes(1));
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);

        var payment = order.ConfirmCash(500m, "user_cashier", T0.AddMinutes(5));
        Assert.Equal(215.00m, payment.ChangeDue);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(T0.AddMinutes(5), order.PaidAt);

        order.StartPreparing("user_kitchen", T0.AddMinutes(6));
        order.MarkReady("user_kitchen", T0.AddMinutes(10));
        order.Complete("user_kitchen", T0.AddMinutes(11));

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(
            [OrderStatus.Created, OrderStatus.AwaitingPayment, OrderStatus.Paid, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed],
            order.Events.Select(e => e.To));
    }

    [Fact]
    public void Cash_less_than_total_is_rejected()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.Cash, T0);
        var ex = Assert.Throws<DomainException>(() => order.ConfirmCash(284.99m, "c", T0));
        Assert.Equal("insufficient_cash", ex.Code);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void Cash_cannot_be_confirmed_after_the_window_closes()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.Cash, T0);
        var ex = Assert.Throws<DomainException>(() => order.ConfirmCash(500m, "c", T0 + Window));
        Assert.Equal("order_expired", ex.Code);
    }

    [Fact]
    public void Illegal_transition_throws_instead_of_writing()
    {
        var order = NewOrder();
        var ex = Assert.Throws<DomainException>(() => order.MarkReady("k", T0));
        Assert.Equal("illegal_transition", ex.Code);
        Assert.Equal(OrderStatus.Created, order.Status);
    }

    [Fact]
    public void Paid_order_cannot_be_cancelled_or_expired()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.Cash, T0);
        order.ConfirmCash(300m, "c", T0);

        Assert.Equal("not_cancellable", Assert.Throws<DomainException>(() => order.Cancel("x", "c", T0)).Code);
        Assert.Equal("not_expirable", Assert.Throws<DomainException>(() => order.Expire(T0 + Window)).Code);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.EWallet)]
    public void Every_pre_paid_state_expires(PaymentMethod method)
    {
        var order = NewOrder();
        order.ChoosePayment(method, T0);

        Assert.Throws<DomainException>(() => order.Expire(T0 + Window - TimeSpan.FromSeconds(1)));
        order.Expire(T0 + Window);
        Assert.Equal(OrderStatus.Expired, order.Status);
    }

    [Fact]
    public void Online_payment_confirmed_by_webhook()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.EWallet, T0);
        var payment = order.StartOnlinePayment("PayMongo", "cs_1", "https://pay", T0);

        Assert.True(order.ConfirmOnlinePayment(payment, T0.AddMinutes(2)));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void Abandoned_checkout_can_retry_or_switch_to_cash()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.EWallet, T0);
        order.StartOnlinePayment("PayMongo", "cs_1", "https://pay", T0);
        order.AbandonOnlinePayment(T0);
        Assert.Equal(OrderStatus.Failed, order.Status);

        order.ChoosePayment(PaymentMethod.Cash, T0.AddMinutes(1));
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public void Late_phone_payment_after_switching_to_cash_still_pays_the_order()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.EWallet, T0);
        var payment = order.StartOnlinePayment("PayMongo", "cs_1", "https://pay", T0);
        order.AbandonOnlinePayment(T0);
        order.ChoosePayment(PaymentMethod.Cash, T0);

        Assert.True(order.ConfirmOnlinePayment(payment, T0.AddMinutes(1)));
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentMethod.EWallet, order.PaymentMethod);
    }

    [Fact]
    public void Payment_on_an_expired_order_does_not_revive_it_and_flags_a_refund()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.EWallet, T0);
        var payment = order.StartOnlinePayment("PayMongo", "cs_1", "https://pay", T0);
        order.Expire(T0 + Window);

        Assert.False(order.ConfirmOnlinePayment(payment, T0 + Window + TimeSpan.FromMinutes(1)));
        Assert.Equal(OrderStatus.Expired, order.Status);
        Assert.Contains(order.Events, e => e.RefundNeeded);
    }

    [Fact]
    public void Cancel_requires_a_reason()
    {
        var order = NewOrder();
        Assert.Equal("reason_required", Assert.Throws<DomainException>(() => order.Cancel(" ", "c", T0)).Code);
        order.Cancel("Customer left", "c", T0);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Payment_method_cannot_be_chosen_twice()
    {
        var order = NewOrder();
        order.ChoosePayment(PaymentMethod.Cash, T0);
        Assert.Equal("payment_already_chosen",
            Assert.Throws<DomainException>(() => order.ChoosePayment(PaymentMethod.Card, T0)).Code);
    }

    [Fact]
    public void State_machine_terminal_states_have_no_exits()
    {
        foreach (var terminal in new[] { OrderStatus.Completed, OrderStatus.Expired, OrderStatus.Cancelled })
        foreach (var to in Enum.GetValues<OrderStatus>())
            Assert.False(OrderStateMachine.CanTransition(terminal, to));
    }
}
