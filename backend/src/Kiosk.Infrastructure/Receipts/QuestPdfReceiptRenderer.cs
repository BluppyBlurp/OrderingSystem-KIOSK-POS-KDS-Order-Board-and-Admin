using System.Globalization;
using Kiosk.Application.Abstractions;
using Kiosk.Application.Receipts;
using Kiosk.Domain.Orders;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kiosk.Infrastructure.Receipts;

/// <summary>
/// 80 mm thermal-roll layout, the same paper the kiosk prints through the browser. ContinuousSize grows the page
/// with the content, like a receipt roll, instead of splitting it across fixed pages.
/// </summary>
internal sealed class QuestPdfReceiptRenderer : IReceiptRenderer
{
    private const float PaperWidthMm = 80;
    private const float MarginMm = 4;

    static QuestPdfReceiptRenderer()
    {
        // Free for organisations under USD 1M annual revenue; see https://www.questpdf.com/license/
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] RenderReceipt(ReceiptModel r) => Roll(column =>
    {
        Header(column, r.StoreName, r.HeaderLines);
        if (r.IsReprint)
            column.Item().AlignCenter().Text("REPRINT").Bold();

        column.Item().PaddingTop(6).AlignCenter().Text("ORDER").FontSize(9);
        column.Item().AlignCenter().Text(r.OrderNumber).FontSize(28).Bold();
        column.Item().AlignCenter().Text(OrderTypeLabel(r.DiningOption, r.Type, r.TableNumber)).SemiBold();
        column.Item().AlignCenter().Text(r.PaidAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).FontSize(8);

        Rule(column);
        Lines(column, r.Lines);
        Rule(column);

        Row(column, "Total", Peso(r.Total), bold: true, size: 12);
        Row(column, $"VAT ({r.VatRate * 100:0.##}%, included)", Peso(r.VatAmount));
        Row(column, "VATable sales", Peso(r.Total - r.VatAmount));

        Rule(column);
        Row(column, "Paid by", PaymentLabel(r.PaymentMethod));
        if (r.AmountTendered is { } tendered)
            Row(column, "Cash received", Peso(tendered));
        if (r.ChangeDue is { } change)
            Row(column, "Change", Peso(change), bold: true);

        column.Item().PaddingTop(10).AlignCenter().Text(r.Footer).FontSize(8);
    });

    public byte[] RenderSlip(SlipModel s) => Roll(column =>
    {
        Header(column, s.StoreName, []);
        column.Item().PaddingTop(4).AlignCenter().Text("PAY AT THE COUNTER").Bold();
        column.Item().AlignCenter().Text(s.OrderNumber).FontSize(32).Bold();
        column.Item().AlignCenter().Text(OrderTypeLabel(s.DiningOption, s.Type, s.TableNumber)).SemiBold();
        column.Item().PaddingTop(4).AlignCenter().Text(Peso(s.Total)).FontSize(18).Bold();

        column.Item().PaddingVertical(6).AlignCenter().Width(40, Unit.Millimetre).Image(QrPng(s.QrToken));
        column.Item().AlignCenter().Text("Show this to the cashier").FontSize(8);
        column.Item().AlignCenter()
            .Text($"Pay by {s.ExpiresAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture)} or the order is cancelled")
            .FontSize(8);

        Rule(column);
        Lines(column, s.Lines);
    });

    private static byte[] Roll(Action<ColumnDescriptor> content) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.ContinuousSize(PaperWidthMm, Unit.Millimetre);
            page.Margin(MarginMm, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Black));
            page.Content().Column(column =>
            {
                column.Spacing(2);
                content(column);
            });
        })).GeneratePdf();

    private static void Header(ColumnDescriptor column, string storeName, IReadOnlyList<string> lines)
    {
        column.Item().AlignCenter().Text(storeName).FontSize(12).Bold();
        foreach (var line in lines)
            column.Item().AlignCenter().Text(line).FontSize(8);
    }

    private static void Lines(ColumnDescriptor column, IReadOnlyList<ReceiptLine> lines)
    {
        foreach (var line in lines)
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"{line.Quantity} × {line.Name}").SemiBold();
                row.AutoItem().Text(Peso(line.LineTotal));
            });
            foreach (var modifier in line.Modifiers)
                column.Item().PaddingLeft(10).Text(modifier).FontSize(8);
            if (!string.IsNullOrEmpty(line.Notes))
                column.Item().PaddingLeft(10).Text($"Note: {line.Notes}").FontSize(8).Italic();
            if (line.Quantity > 1)
                column.Item().PaddingLeft(10).Text($"{Peso(line.UnitPrice)} each").FontSize(7);
        }
    }

    private static void Row(ColumnDescriptor column, string label, string value, bool bold = false, float size = 9)
    {
        column.Item().Row(row =>
        {
            var left = row.RelativeItem().Text(label).FontSize(size);
            var right = row.AutoItem().Text(value).FontSize(size);
            if (bold)
            {
                left.Bold();
                right.Bold();
            }
        });
    }

    private static void Rule(ColumnDescriptor column) =>
        column.Item().PaddingVertical(3).LineHorizontal(0.5f).LineColor(Colors.Black);

    private static byte[] QrPng(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10);
    }

    internal static string Peso(decimal amount) => "₱" + amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    internal static string OrderTypeLabel(DiningOption dining, OrderType type, int? table) => (dining, type) switch
    {
        (DiningOption.TakeOut, _) => "Take out",
        (_, OrderType.ServeToTable) => $"Dine in · Table {table}",
        _ => "Dine in · Counter pickup",
    };

    internal static string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.EWallet => "E-wallet (GCash / Maya)",
        PaymentMethod.Card => "Card",
        PaymentMethod.QrPh => "QR Ph",
        _ => method.ToString(),
    };
}
