using FluentValidation;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Application.Payments;
using Kiosk.Domain.Common;
using Kiosk.Domain.Menu;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kiosk.Application.Orders;

public sealed class KioskOrderService(
    IAppDbContext db,
    IValidator<CreateOrderRequest> createValidator,
    IValidator<PayRequest> payValidator,
    IValidator<CardTapRequest> cardTapValidator,
    IOrderNumberGenerator numbers,
    IPaymentGateway gateway,
    PaymentWebhookService payments,
    ISlipTokenService slipTokens,
    IOrderNotifier notifier,
    BusinessClock clock,
    IOptions<OrderingOptions> options,
    ILogger<KioskOrderService> logger)
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

        string? checkoutUrl = null, qrImage = null, qrTestUrl = null, cardPublicKey = null;
        switch (request.Method)
        {
            case PaymentMethod.Cash:
                break;
            case PaymentMethod.QrPh:
                var qr = await gateway.CreateQrPhAsync(order, ct);
                order.StartOnlinePayment(gateway.Name, qr.ProviderRef, checkoutUrl: null, now);
                (qrImage, qrTestUrl) = (qr.ImageDataUri, qr.TestUrl);
                break;
            case PaymentMethod.Card:
                var card = await gateway.CreateCardPaymentAsync(order, ct);
                order.StartOnlinePayment(gateway.Name, card.ProviderRef, checkoutUrl: null, now);
                cardPublicKey = card.PublicKey;
                break;
            default:
                var session = await gateway.CreateCheckoutAsync(order, request.Method, ct);
                order.StartOnlinePayment(gateway.Name, session.ProviderRef, session.CheckoutUrl, now);
                checkoutUrl = session.CheckoutUrl;
                break;
        }

        await db.SaveChangesAsync(ct);

        var dto = OrderDto.From(order);
        await notifier.OrderChangedAsync(dto, ct);
        return new KioskOrderDto(dto, SlipTokenFor(order), checkoutUrl, qrImage, qrTestUrl, cardPublicKey);
    }

    /// <summary>
    /// The reader read a card and turned it into a gateway payment method. This API attaches it with the secret key
    /// and applies the gateway's answer straight away, the way a card terminal settles; the webhook later finds the
    /// payment already settled. When the bank wants 3DS, CheckoutUrl carries its page for the customer's phone.
    /// </summary>
    public async Task<KioskOrderDto> TapCardAsync(Guid orderId, CardTapRequest request, Guid deviceId, CancellationToken ct)
    {
        await cardTapValidator.ValidateAndThrowAsync(request, ct);

        var order = await GetOwnedAsync(orderId, deviceId, ct);
        if (order.Status != OrderStatus.PaymentPending || order.PaymentMethod != PaymentMethod.Card)
            throw new DomainException("not_waiting_for_card", "This order isn't waiting for a card.");
        var payment = PendingPayment(order)
            ?? throw new DomainException("not_waiting_for_card", "This order isn't waiting for a card.");

        var result = await gateway.AttachCardAsync(payment.ProviderRef!, request.PaymentMethodId, order.Id, ct);
        if (result.Status == IntentStatus.NeedsAction)
        {
            payment.CheckoutUrl = result.ActionUrl;
            await db.SaveChangesAsync(ct);
        }
        else
        {
            await ApplyAsync(payment.ProviderRef!, result, $"attach_{payment.ProviderRef}_{request.PaymentMethodId}", ct);
        }

        return await GetAsync(orderId, deviceId, ct);
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

    /// <summary>
    /// Status poll fallback. A kiosk can only read orders it created. While an online payment is open it also asks
    /// the gateway, so a payment still settles when the webhook is slow, lost, or cannot reach this API (local dev).
    /// </summary>
    public async Task<KioskOrderDto> GetAsync(Guid orderId, Guid deviceId, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, deviceId, ct);
        var pending = PendingPayment(order);

        if (order.Status == OrderStatus.PaymentPending && pending?.ProviderRef is { } reference)
        {
            IntentResult? result = null;
            try { result = await gateway.GetIntentAsync(reference, ct); }
            catch (HttpRequestException ex) { logger.LogWarning(ex, "Could not check payment {Ref}", reference); }

            if (result is { Status: IntentStatus.Succeeded or IntentStatus.Failed })
            {
                await ApplyAsync(reference, result, $"poll_{reference}_{result.Status}", ct);
                order = await LoadAsync(orderId, deviceId, ct);
                pending = PendingPayment(order);
            }
        }

        var checkoutUrl = order.Status == OrderStatus.PaymentPending ? pending?.CheckoutUrl : null;
        return new KioskOrderDto(OrderDto.From(order), SlipTokenFor(order), checkoutUrl);
    }

    /// <summary>Applies a gateway answer through the same once-only path as a webhook.</summary>
    private Task ApplyAsync(string providerRef, IntentResult result, string eventId, CancellationToken ct) =>
        payments.HandleAsync(new PaymentEvent(
            gateway.Name, eventId, "api.payment_intent", result.Status == IntentStatus.Succeeded, providerRef,
            result.Amount, result.FailureReason), ct);

    private async Task<Order> LoadAsync(Guid orderId, Guid deviceId, CancellationToken ct) =>
        await db.Orders.AsNoTracking().WithDetails()
            .FirstOrDefaultAsync(o => o.Id == orderId && o.DeviceId == deviceId, ct)
        ?? throw new NotFoundException("Order not found.");

    private static Payment? PendingPayment(Order order) =>
        order.Payments
            .Where(p => p.Status == PaymentStatus.Pending)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();

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
