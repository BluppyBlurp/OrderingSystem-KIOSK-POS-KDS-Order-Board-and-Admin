using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Domain.Common;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Kiosk.Application.Orders;

public sealed record ConfirmCashRequest(decimal AmountTendered);

public sealed record CancelOrderRequest(string Reason);

public sealed record CashConfirmationDto(OrderDto Order, decimal AmountTendered, decimal ChangeDue);

public sealed class PosService(
    IAppDbContext db,
    ISlipTokenService slipTokens,
    IOrderNotifier notifier,
    BusinessClock clock)
{
    public async Task<IReadOnlyList<OrderDto>> GetPendingCashAsync(CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking().WithDetails()
            .Where(o => o.Status == OrderStatus.AwaitingPayment)
            .OrderBy(o => o.CreatedAt)
            .ToListAsync(ct);
        return orders.Select(OrderDto.From).ToList();
    }

    /// <summary>Finds an order from a scanned slip QR token or a typed order number (today's).</summary>
    public async Task<OrderDto> LookupAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NotFoundException("Enter an order number or scan the slip.");

        Order? order;
        if (slipTokens.Read(code.Trim()) is { } orderId)
        {
            order = await db.Orders.AsNoTracking().WithDetails().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        }
        else if (OrderNumber.Normalize(code) is { } number)
        {
            var today = clock.Today;
            order = await db.Orders.AsNoTracking().WithDetails()
                .FirstOrDefaultAsync(o => o.OrderNumber == number && o.BusinessDate == today, ct);
        }
        else
        {
            order = null;
        }

        return order is null ? throw new NotFoundException("No order matches that code.") : OrderDto.From(order);
    }

    public async Task<CashConfirmationDto> ConfirmCashAsync(Guid orderId, ConfirmCashRequest request, string cashierId, CancellationToken ct)
    {
        if (request.AmountTendered <= 0 || request.AmountTendered > 1_000_000)
            throw new DomainException("invalid_amount", "Enter the cash amount received.");

        var order = await db.GetTrackedAsync(orderId, ct);
        var payment = order.ConfirmCash(Money.Round(request.AmountTendered), cashierId, clock.Now);
        await db.SaveChangesAsync(ct);

        var dto = OrderDto.From(order);
        await notifier.OrderChangedAsync(dto, ct);
        return new CashConfirmationDto(dto, payment.AmountTendered!.Value, payment.ChangeDue!.Value);
    }

    public async Task<OrderDto> CancelAsync(Guid orderId, CancelOrderRequest request, string actorId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.GetTrackedAsync(orderId, ct);
        order.Cancel(request.Reason, actorId, clock.Now);
        await StockLedger.ReleaseAsync(db, order, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var dto = OrderDto.From(order);
        await notifier.OrderChangedAsync(dto, ct);
        return dto;
    }
}

public sealed class KdsService(IAppDbContext db, IOrderNotifier notifier, BusinessClock clock)
{
    private static readonly OrderStatus[] KitchenStatuses = [OrderStatus.Paid, OrderStatus.Preparing, OrderStatus.Ready];

    public async Task<IReadOnlyList<OrderDto>> GetActiveAsync(CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking().WithDetails()
            .Where(o => KitchenStatuses.Contains(o.Status))
            .OrderBy(o => o.PaidAt)
            .ToListAsync(ct);
        return orders.Select(OrderDto.From).ToList();
    }

    public Task<OrderDto> StartPreparingAsync(Guid id, string actorId, CancellationToken ct) =>
        MoveAsync(id, o => o.StartPreparing(actorId, clock.Now), ct);

    public Task<OrderDto> MarkReadyAsync(Guid id, string actorId, CancellationToken ct) =>
        MoveAsync(id, o => o.MarkReady(actorId, clock.Now), ct);

    public Task<OrderDto> CompleteAsync(Guid id, string actorId, CancellationToken ct) =>
        MoveAsync(id, o => o.Complete(actorId, clock.Now), ct);

    private async Task<OrderDto> MoveAsync(Guid id, Action<Order> transition, CancellationToken ct)
    {
        var order = await db.GetTrackedAsync(id, ct);
        transition(order);
        await db.SaveChangesAsync(ct);

        var dto = OrderDto.From(order);
        await notifier.OrderChangedAsync(dto, ct);
        return dto;
    }
}

public sealed record BoardEntryDto(string OrderNumber, DiningOption DiningOption, OrderType Type, int? TableNumber);

public sealed record BoardDto(IReadOnlyList<BoardEntryDto> Preparing, IReadOnlyList<BoardEntryDto> Ready);

public sealed class BoardService(IAppDbContext db)
{
    public async Task<BoardDto> GetAsync(CancellationToken ct)
    {
        var rows = await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.Ready)
            .OrderBy(o => o.PaidAt)
            .Select(o => new { o.Status, Entry = new BoardEntryDto(o.OrderNumber, o.DiningOption, o.Type, o.TableNumber) })
            .ToListAsync(ct);

        return new BoardDto(
            rows.Where(r => r.Status != OrderStatus.Ready).Select(r => r.Entry).ToList(),
            rows.Where(r => r.Status == OrderStatus.Ready).Select(r => r.Entry).ToList());
    }
}
