using Kiosk.Application.Abstractions;
using Kiosk.Domain.Common;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Application.Orders;

/// <summary>
/// Stock is reserved when an order is created and released when an unpaid order expires or is cancelled.
/// Each call is a single conditional UPDATE, so it is race-free; run it inside the order's transaction.
/// </summary>
internal static class StockLedger
{
    public static async Task ReserveAsync(IAppDbContext db, IEnumerable<OrderItem> items, CancellationToken ct)
    {
        // Lock rows in a stable order so two concurrent orders cannot deadlock.
        foreach (var line in Group(items))
        {
            var qty = line.Quantity;
            var updated = await db.Products
                .Where(p => p.Id == line.ProductId && p.IsAvailable && (p.Stock == null || p.Stock >= qty))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - qty), ct);

            if (updated == 0)
                throw new DomainException("sold_out", $"Sorry, {line.Name} just sold out.");
        }
    }

    public static async Task ReleaseAsync(IAppDbContext db, Order order, CancellationToken ct)
    {
        foreach (var line in Group(order.Items))
        {
            var qty = line.Quantity;
            await db.Products
                .Where(p => p.Id == line.ProductId && p.Stock != null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + qty), ct);
        }
    }

    private static IEnumerable<(Guid ProductId, string Name, int Quantity)> Group(IEnumerable<OrderItem> items) =>
        items.GroupBy(i => i.ProductId)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.First().NameSnapshot, g.Sum(i => i.Quantity)));
}
