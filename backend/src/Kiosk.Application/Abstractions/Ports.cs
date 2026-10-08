using Kiosk.Application.Orders;
using Kiosk.Domain.Orders;

namespace Kiosk.Application.Abstractions;

/// <summary>Concurrency-safe daily counter. Must run inside the caller's transaction.</summary>
public interface IOrderNumberGenerator
{
    Task<int> NextAsync(DateOnly businessDate, CancellationToken ct);
}

/// <summary>Pushes realtime updates (SignalR). Call only after the transaction commits.</summary>
public interface IOrderNotifier
{
    Task OrderChangedAsync(OrderDto order, CancellationToken ct = default);
    Task MenuChangedAsync(CancellationToken ct = default);
}

public sealed record CheckoutSession(string ProviderRef, string CheckoutUrl);

public interface IPaymentGateway
{
    string Name { get; }
    Task<CheckoutSession> CreateCheckoutAsync(Order order, PaymentMethod method, CancellationToken ct);
}

/// <summary>Short signed token for the cash slip QR, so a screenshot cannot be replayed against another order.</summary>
public interface ISlipTokenService
{
    string Create(Guid orderId, DateTimeOffset expiresAt);
    Guid? Read(string token);
}

/// <summary>Who is making the current change (staff user id or device id). Used for the audit log.</summary>
public interface ICurrentActor
{
    string? ActorId { get; }
}
