using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Application.Orders;

internal static class OrderQueries
{
    public static IQueryable<Order> WithDetails(this IQueryable<Order> orders) =>
        orders.Include(o => o.Items).ThenInclude(i => i.Modifiers).Include(o => o.Payments);

    public static async Task<Order> GetTrackedAsync(this IAppDbContext db, Guid id, CancellationToken ct) =>
        await db.Orders.WithDetails().FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new NotFoundException("Order not found.");
}
