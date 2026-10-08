using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Domain.Common;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Application.Reports;

public sealed record CashTotalsDto(int Orders, decimal CashCollected);

/// <summary>What a cashier counts the drawer against: their own cash orders, and the whole counter's.</summary>
public sealed record ShiftSummaryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    CashTotalsDto Mine,
    CashTotalsDto AllCashiers,
    int CancelledByMe);

public sealed record PaymentMethodSalesDto(PaymentMethod Method, int Orders, decimal Sales);

public sealed record DailySalesDto(DateOnly Date, int Orders, decimal Sales);

public sealed record ProductSalesDto(Guid ProductId, string Name, int Quantity, decimal Sales);

public sealed record SalesReportDto(
    DateOnly From,
    DateOnly To,
    int Orders,
    decimal GrossSales,
    decimal VatAmount,
    decimal NetOfVat,
    decimal AverageOrder,
    IReadOnlyList<PaymentMethodSalesDto> ByPaymentMethod,
    IReadOnlyList<DailySalesDto> ByDay,
    IReadOnlyList<ProductSalesDto> TopProducts,
    int RefundsNeeded);

/// <summary>A payment that landed on an order that could not take it (expired, cancelled, or the wrong amount).</summary>
public sealed record RefundNeededDto(
    long EventId,
    Guid OrderId,
    string OrderNumber,
    DateOnly BusinessDate,
    OrderStatus OrderStatus,
    decimal OrderTotal,
    DateTimeOffset At,
    string? Note);

public sealed class ReportService(IAppDbContext db, BusinessClock clock)
{
    public const int MaxReportDays = 366;
    private const int TopProductCount = 10;

    /// <summary>Statuses an order can only reach by being paid; Expired/Cancelled orders never count as sales.</summary>
    private static readonly OrderStatus[] SoldStatuses =
        [OrderStatus.Paid, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed];

    /// <summary>Cash taken since <paramref name="since"/> (default: start of today's business date) until now.</summary>
    public async Task<ShiftSummaryDto> GetShiftSummaryAsync(string cashierId, DateTimeOffset? since, CancellationToken ct)
    {
        var to = clock.Now;
        var from = since?.ToUniversalTime() ?? clock.StartOf(clock.Today);
        if (from > to)
            throw new DomainException("invalid_range", "The shift cannot start in the future.");

        var cash = await db.Payments.AsNoTracking()
            .Where(p => p.Method == PaymentMethod.Cash && p.Status == PaymentStatus.Succeeded)
            .Where(p => p.CompletedAt >= from && p.CompletedAt <= to)
            .Select(p => new { p.ProcessedByUserId, p.Amount })
            .ToListAsync(ct);

        var cancelled = await db.OrderEvents.AsNoTracking()
            .CountAsync(e => e.To == OrderStatus.Cancelled && e.ActorId == cashierId && e.At >= from && e.At <= to, ct);

        var mine = cash.Where(p => p.ProcessedByUserId == cashierId).ToList();
        return new ShiftSummaryDto(
            from, to,
            new CashTotalsDto(mine.Count, mine.Sum(p => p.Amount)),
            new CashTotalsDto(cash.Count, cash.Sum(p => p.Amount)),
            cancelled);
    }

    /// <summary>Sales by business date, inclusive. Defaults to today.</summary>
    public async Task<SalesReportDto> GetSalesAsync(DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        var to = toDate ?? clock.Today;
        var from = fromDate ?? to;
        if (from > to)
            throw new DomainException("invalid_range", "The start date must be on or before the end date.");
        if (to.DayNumber - from.DayNumber >= MaxReportDays)
            throw new DomainException("range_too_long", $"Report on at most {MaxReportDays} days at a time.");

        var sold = db.Orders.AsNoTracking()
            .Where(o => o.BusinessDate >= from && o.BusinessDate <= to && SoldStatuses.Contains(o.Status));

        var orders = await sold
            .Select(o => new { o.BusinessDate, o.Total, o.TaxAmount, o.PaymentMethod })
            .ToListAsync(ct);

        var topProducts = await sold
            .SelectMany(o => o.Items)
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Name = g.Max(i => i.NameSnapshot)!, Quantity = g.Sum(i => i.Quantity), Sales = g.Sum(i => i.LineTotal) })
            .OrderByDescending(p => p.Quantity).ThenByDescending(p => p.Sales)
            .Take(TopProductCount)
            .ToListAsync(ct);

        var refunds = await RefundEvents()
            .CountAsync(x => x.Order.BusinessDate >= from && x.Order.BusinessDate <= to, ct);

        var gross = orders.Sum(o => o.Total);
        var vat = orders.Sum(o => o.TaxAmount);
        return new SalesReportDto(
            from, to,
            orders.Count, gross, vat, gross - vat,
            orders.Count == 0 ? 0m : Money.Round(gross / orders.Count),
            orders.GroupBy(o => o.PaymentMethod!.Value)
                .Select(g => new PaymentMethodSalesDto(g.Key, g.Count(), g.Sum(o => o.Total)))
                .OrderByDescending(m => m.Sales)
                .ToList(),
            orders.GroupBy(o => o.BusinessDate)
                .Select(g => new DailySalesDto(g.Key, g.Count(), g.Sum(o => o.Total)))
                .OrderBy(d => d.Date)
                .ToList(),
            topProducts.Select(p => new ProductSalesDto(p.ProductId, p.Name, p.Quantity, p.Sales)).ToList(),
            refunds);
    }

    /// <summary>Newest first. These need a manual refund through the payment provider's dashboard.</summary>
    public async Task<IReadOnlyList<RefundNeededDto>> GetRefundsNeededAsync(CancellationToken ct) =>
        await RefundEvents()
            .OrderByDescending(x => x.Event.At)
            .Take(200)
            .Select(x => new RefundNeededDto(
                x.Event.Id, x.Order.Id, x.Order.OrderNumber, x.Order.BusinessDate, x.Order.Status, x.Order.Total,
                x.Event.At, x.Event.Note))
            .ToListAsync(ct);

    private IQueryable<RefundEvent> RefundEvents() =>
        from e in db.OrderEvents.AsNoTracking()
        where e.RefundNeeded
        join o in db.Orders.AsNoTracking() on e.OrderId equals o.Id
        select new RefundEvent { Event = e, Order = o };

    private sealed class RefundEvent
    {
        public required OrderEvent Event { get; init; }
        public required Order Order { get; init; }
    }
}
