using FluentValidation;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Domain.Common;
using Kiosk.Domain.Menu;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kiosk.Application.Orders;

public sealed class KioskOrderService(
    IAppDbContext db,
    IValidator<CreateOrderRequest> createValidator,
    IValidator<PayRequest> payValidator,
    IOrderNumberGenerator numbers,
    IPaymentGateway gateway,
    ISlipTokenService slipTokens,
    IOrderNotifier notifier,
    BusinessClock clock,
    IOptions<OrderingOptions> options)
{
    /// <summary>
    /// Builds the order from database prices (client prices are never accepted), reserves stock,
    /// and assigns the daily order number — all in one transaction.
    /// </summary>
    public async Task<KioskOrderDto> CreateAsync(CreateOrderRequest request, Guid deviceId, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.ModifierGroups).ThenInclude(pmg => pmg.ModifierGroup!).ThenInclude(g => g.Modifiers)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        var items = request.Items.Select(line => BuildItem(line, products)).ToList();

        var opts = options.Value;
        var now = clock.Now;
        var businessDate = clock.Today;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await StockLedger.ReserveAsync(db, items, ct);
        var sequence = await numbers.NextAsync(businessDate, ct);

        var order = Order.Create(
            OrderNumber.Format(sequence), businessDate, request.DiningOption, request.OrderType, request.TableNumber, deviceId,
            items, opts.VatRate, now, TimeSpan.FromMinutes(opts.PaymentWindowMinutes));

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new KioskOrderDto(OrderDto.From(order), null, null);
    }

    public async Task<KioskOrderDto> PayAsync(Guid orderId, PayRequest request, Guid deviceId, CancellationToken ct)
    {
        await payValidator.ValidateAndThrowAsync(request, ct);

        var order = await GetOwnedAsync(orderId, deviceId, ct);
        var now = clock.Now;
        order.ChoosePayment(request.Method, now);

        string? checkoutUrl = null;
        if (request.Method != PaymentMethod.Cash)
        {
            var session = await gateway.CreateCheckoutAsync(order, request.Method, ct);
            order.StartOnlinePayment(gateway.Name, session.ProviderRef, session.CheckoutUrl, now);
            checkoutUrl = session.CheckoutUrl;
        }

        await db.SaveChangesAsync(ct);

        var dto = OrderDto.From(order);
        await notifier.OrderChangedAsync(dto, ct);
        return new KioskOrderDto(dto, SlipTokenFor(order), checkoutUrl);
    }

    /// <summary>Customer backed out of the e-wallet/card checkout; the kiosk then offers Retry or Switch to cash.</summary>
    public async Task<KioskOrderDto> CancelCheckoutAsync(Guid orderId, Guid deviceId, CancellationToken ct)
    {
        var order = await GetOwnedAsync(orderId, deviceId, ct);
        order.AbandonOnlinePayment(clock.Now);
        await db.SaveChangesAsync(ct);

        var dto = OrderDto.From(order);
        await notifier.OrderChangedAsync(dto, ct);
        return new KioskOrderDto(dto, null, null);
    }

    /// <summary>Status poll fallback. A kiosk can only read orders it created.</summary>
    public async Task<KioskOrderDto> GetAsync(Guid orderId, Guid deviceId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().WithDetails()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeviceId == deviceId, ct)
            ?? throw new NotFoundException("Order not found.");

        var pending = order.Payments
            .Where(p => p.Status == PaymentStatus.Pending)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();
        var checkoutUrl = order.Status == OrderStatus.PaymentPending ? pending?.CheckoutUrl : null;
        return new KioskOrderDto(OrderDto.From(order), SlipTokenFor(order), checkoutUrl);
    }

    /// <summary>Drives the soft "table already in use" warning; never blocks the order.</summary>
    public async Task<TableStatusDto> GetTableStatusAsync(int tableNumber, CancellationToken ct)
    {
        var inUse = await db.Orders.AnyAsync(o =>
            o.TableNumber == tableNumber &&
            o.Status != OrderStatus.Completed &&
            o.Status != OrderStatus.Expired &&
            o.Status != OrderStatus.Cancelled, ct);
        return new TableStatusDto(tableNumber, inUse);
    }

    public async Task<bool> IsOwnedByAsync(Guid orderId, Guid deviceId, CancellationToken ct) =>
        await db.Orders.AnyAsync(o => o.Id == orderId && o.DeviceId == deviceId, ct);

    private string? SlipTokenFor(Order order) =>
        order.Status == OrderStatus.AwaitingPayment ? slipTokens.Create(order.Id, order.ExpiresAt) : null;

    private async Task<Order> GetOwnedAsync(Guid orderId, Guid deviceId, CancellationToken ct) =>
        await db.Orders.WithDetails().FirstOrDefaultAsync(o => o.Id == orderId && o.DeviceId == deviceId, ct)
        ?? throw new NotFoundException("Order not found.");

    private static OrderItem BuildItem(CreateOrderLine line, IReadOnlyDictionary<Guid, Product> products)
    {
        if (!products.TryGetValue(line.ProductId, out var product))
            throw new DomainException("unknown_product", "An item in the cart is no longer on the menu.");
        if (!product.IsAvailable || product.Category is { IsActive: false })
            throw new DomainException("unavailable", $"Sorry, {product.Name} is not available right now.");

        var modifiers = ModifierRules.Resolve(product, line.ModifierIds ?? []);
        return new OrderItem
        {
            ProductId = product.Id,
            NameSnapshot = product.Name,
            UnitPriceSnapshot = Money.Round(product.BasePrice + modifiers.Sum(m => m.PriceDelta)),
            Quantity = line.Quantity,
            Notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim(),
            Modifiers = modifiers.Select((m, i) => new OrderItemModifier
            {
                SortOrder = i,
                ModifierId = m.Id,
                NameSnapshot = m.Name,
                PriceDeltaSnapshot = m.PriceDelta,
            }).ToList(),
        };
    }
}
