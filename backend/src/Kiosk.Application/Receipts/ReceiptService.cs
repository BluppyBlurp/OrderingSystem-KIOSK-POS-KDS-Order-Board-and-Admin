using Kiosk.Application.Abstractions;
using Kiosk.Application.Common;
using Kiosk.Application.Orders;
using Kiosk.Domain.Common;
using Kiosk.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kiosk.Application.Receipts;

public sealed record ReceiptLine(string Name, int Quantity, decimal UnitPrice, decimal LineTotal, IReadOnlyList<string> Modifiers, string? Notes);

public sealed record ReceiptModel(
    string StoreName,
    IReadOnlyList<string> HeaderLines,
    string Footer,
    string OrderNumber,
    DateTimeOffset PaidAtLocal,
    DiningOption DiningOption,
    OrderType Type,
    int? TableNumber,
    IReadOnlyList<ReceiptLine> Lines,
    decimal Subtotal,
    decimal VatAmount,
    decimal VatRate,
    decimal Total,
    PaymentMethod PaymentMethod,
    decimal? AmountTendered,
    decimal? ChangeDue,
    bool IsReprint);

public sealed record SlipModel(
    string StoreName,
    string OrderNumber,
    DiningOption DiningOption,
    OrderType Type,
    int? TableNumber,
    IReadOnlyList<ReceiptLine> Lines,
    decimal Total,
    string QrToken,
    DateTimeOffset ExpiresAtLocal);

/// <summary>A rendered PDF plus the file name the browser should save it under.</summary>
public sealed record PdfFile(byte[] Content, string FileName);

/// <summary>PDF receipts (cashier reprints) and cash slips (kiosk, for a future printer).</summary>
public sealed class ReceiptService(
    IAppDbContext db,
    IReceiptRenderer renderer,
    ISlipTokenService slipTokens,
    BusinessClock clock,
    IOptions<ReceiptOptions> receiptOptions,
    IOptions<OrderingOptions> orderingOptions)
{
    /// <summary>Paid receipt. Every request after the first print is a reprint, so the PDF always says so.</summary>
    public async Task<PdfFile> GetReceiptAsync(Guid orderId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().WithDetails().FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new NotFoundException("Order not found.");
        if (order.PaidAt is null)
            throw new DomainException("not_paid", $"Order {order.OrderNumber} is {order.Status}; only paid orders have a receipt.");

        var payment = order.Payments
            .Where(p => p.Status == PaymentStatus.Succeeded)
            .OrderBy(p => p.CompletedAt)
            .First();
        var o = receiptOptions.Value;
        var model = new ReceiptModel(
            o.StoreName, o.HeaderLines, o.Footer,
            order.OrderNumber, clock.ToLocal(order.PaidAt.Value),
            order.DiningOption, order.Type, order.TableNumber,
            Lines(order), order.Subtotal, order.TaxAmount, orderingOptions.Value.VatRate, order.Total,
            payment.Method, payment.AmountTendered, payment.ChangeDue,
            IsReprint: true);
        return new PdfFile(renderer.RenderReceipt(model), $"receipt-{order.OrderNumber}.pdf");
    }

    /// <summary>Cash slip for an order this kiosk created that is waiting for cash at the counter.</summary>
    public async Task<PdfFile> GetSlipAsync(Guid orderId, Guid deviceId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().WithDetails()
                        .FirstOrDefaultAsync(o => o.Id == orderId && o.DeviceId == deviceId, ct)
                    ?? throw new NotFoundException("Order not found.");
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new DomainException("not_awaiting_cash", $"Order is {order.Status}; a slip is only for orders paying at the counter.");

        var model = new SlipModel(
            receiptOptions.Value.StoreName, order.OrderNumber, order.DiningOption, order.Type, order.TableNumber,
            Lines(order), order.Total, slipTokens.Create(order.Id, order.ExpiresAt), clock.ToLocal(order.ExpiresAt));
        return new PdfFile(renderer.RenderSlip(model), $"slip-{order.OrderNumber}.pdf");
    }

    private static List<ReceiptLine> Lines(Order order) =>
        order.Items.OrderBy(i => i.LineNumber).Select(i => new ReceiptLine(
            i.NameSnapshot, i.Quantity, i.UnitPriceSnapshot, i.LineTotal,
            i.Modifiers.OrderBy(m => m.SortOrder).Select(m => m.NameSnapshot).ToList(),
            i.Notes)).ToList();
}
