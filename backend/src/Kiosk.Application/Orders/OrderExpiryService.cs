using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kiosk.Application.Orders;

/// <summary>Expires every pre-Paid order past its window and releases the stock it reserved.</summary>
public sealed class OrderExpiryService(
    IAppDbContext db,
    IOrderNotifier notifier,
    BusinessClock clock,
    ILogger<OrderExpiryService> logger)
{
    public async Task<int> ExpireDueAsync(CancellationToken ct)
    {
        var now = clock.Now;
        var dueIds = await db.Orders.AsNoTracking()
            .Where(o => OrderStateMachine.PrePaid.Contains(o.Status) && o.ExpiresAt <= now)
            .OrderBy(o => o.ExpiresAt)
            .Select(o => o.Id)
            .Take(100)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var id in dueIds)
        {
            db.ChangeTracker.Clear();
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var order = await db.GetTrackedAsync(id, ct);
                if (!order.IsPrePaid || order.ExpiresAt > now)
                    continue; // paid or changed since we looked

                order.Expire(now);
                await StockLedger.ReleaseAsync(db, order, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                expired++;
                await notifier.OrderChangedAsync(OrderDto.From(order), ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A payment landed at the same moment; the payment wins and the order is not expired.
                logger.LogInformation("Order {OrderId} changed while expiring; skipped", id);
            }
        }

        return expired;
    }
}
