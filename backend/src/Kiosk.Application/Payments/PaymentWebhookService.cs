using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Application.Orders;
using Kiosk.Domain.Common;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Kiosk.Application.Payments;

public enum WebhookOutcome { Applied, Duplicate, Ignored, Unmatched }

/// <summary>A gateway event already parsed and signature-verified by the infrastructure layer.</summary>
public sealed record PaymentEvent(
    string Provider,
    string EventId,
    string EventType,
    bool Succeeded,
    string ProviderRef,
    decimal? Amount,
    string? FailureReason);

/// <summary>
/// The webhook is the only thing that marks an online payment Paid. Each provider event id is applied
/// at most once: the idempotency record is written in the same transaction as the status change.
/// </summary>
public sealed class PaymentWebhookService(
    IAppDbContext db,
    IOrderNotifier notifier,
    BusinessClock clock,
    ILogger<PaymentWebhookService> logger)
{
    public async Task<WebhookOutcome> HandleAsync(PaymentEvent evt, CancellationToken ct)
    {
        if (await db.ProcessedWebhookEvents.AnyAsync(e => e.Id == evt.EventId, ct))
            return WebhookOutcome.Duplicate;

        var now = clock.Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
        {
            Id = evt.EventId, Provider = evt.Provider, EventType = evt.EventType, ProcessedAt = now,
        });

        var payment = await db.Payments.FirstOrDefaultAsync(
            p => p.Provider == evt.Provider && p.ProviderRef == evt.ProviderRef, ct);
        if (payment is null)
        {
            logger.LogWarning("{Provider} event {EventId} references unknown payment {Ref}", evt.Provider, evt.EventId, evt.ProviderRef);
            await SaveAsync(tx, ct);
            return WebhookOutcome.Unmatched;
        }

        var order = await db.GetTrackedAsync(payment.OrderId, ct);
        payment = order.Payments.Single(p => p.Id == payment.Id);

        if (payment.Status != PaymentStatus.Pending)
        {
            await SaveAsync(tx, ct);
            return WebhookOutcome.Ignored;
        }

        if (evt.Succeeded && evt.Amount is { } amount && Money.Round(amount) != payment.Amount)
        {
            // Never mark Paid on a mismatched amount; leave it for a human.
            logger.LogError("Order {Order}: paid {Paid} but expected {Expected}", order.OrderNumber, amount, payment.Amount);
            order.Events.Add(new OrderEvent
            {
                OrderId = order.Id, At = now, From = order.Status, To = order.Status, RefundNeeded = true,
                Note = $"Amount mismatch: gateway reported {amount:0.00}, expected {payment.Amount:0.00}",
            });
            await SaveAsync(tx, ct);
            return WebhookOutcome.Ignored;
        }

        if (evt.Succeeded)
        {
            if (!order.ConfirmOnlinePayment(payment, now))
                logger.LogWarning("Order {Order} paid after it was {Status}; flagged for refund", order.OrderNumber, order.Status);
        }
        else
        {
            order.FailOnlinePayment(payment, evt.FailureReason, now);
        }

        if (!await SaveAsync(tx, ct))
            return WebhookOutcome.Duplicate;

        await notifier.OrderChangedAsync(OrderDto.From(order), ct);
        return WebhookOutcome.Applied;
    }

    /// <returns>false if a concurrent delivery of the same event won the race.</returns>
    private async Task<bool> SaveAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);
            return false;
        }
    }
}
