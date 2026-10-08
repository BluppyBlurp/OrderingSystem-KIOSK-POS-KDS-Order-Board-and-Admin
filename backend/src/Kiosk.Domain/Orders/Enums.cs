namespace Kiosk.Domain.Orders;

public enum DiningOption { DineIn, TakeOut }

/// <summary>How the food reaches the customer. Take-out orders are always counter pickup.</summary>
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

/// <summary>QrPh = the national QR standard; any bank or e-wallet app can pay it.</summary>
public enum PaymentMethod { Cash, EWallet, Card, QrPh }

public enum PaymentStatus { Pending, Succeeded, Failed }
