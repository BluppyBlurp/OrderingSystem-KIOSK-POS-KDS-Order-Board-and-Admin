namespace Kiosk.Domain.Orders;

public enum OrderType { CounterPickup, ServeToTable }

public enum OrderStatus
{
    Created,
    AwaitingPayment,
    PaymentPending,
    Failed,
    Paid,
    Preparing,
    Ready,
    Completed,
    Expired,
    Cancelled,
}

public enum PaymentMethod { Cash, EWallet, Card }

public enum PaymentStatus { Pending, Succeeded, Failed }
