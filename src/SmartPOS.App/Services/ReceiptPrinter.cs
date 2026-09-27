using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using SmartPOS.Core.Entities;

namespace SmartPOS.App.Services;

public sealed class ReceiptPrinter
{
    public bool Print(Window owner, Sale sale, string storeName, string? address, string? phone, string cashierName, string currency, double paperWidthMm = 80)
    {
        ArgumentNullException.ThrowIfNull(sale);
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (dialog.ShowDialog() != true) return false;
        var width = Math.Min(dialog.PrintableAreaWidth, paperWidthMm * 96 / 25.4);
        if (width < 120) width = 302; // 80 mm DIP fallback for printer drivers that report no media size.
        var fontSize = width < 250 ? 9.5 : 10.5;
        var lineHeight = fontSize * 1.55;
        var lines = new List<(string Text, bool Bold, bool Center)>
        {
            (storeName, true, true)
        };
        if (!string.IsNullOrWhiteSpace(address)) lines.Add((address.Trim(), false, true));
        if (!string.IsNullOrWhiteSpace(phone)) lines.Add((phone.Trim(), false, true));
        lines.Add((new string('-', 32), false, false));
        lines.Add(($"Invoice: {sale.InvoiceNumber}", false, false));
        lines.Add(($"Date: {sale.SoldAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}", false, false));
        lines.Add(($"Cashier: {cashierName}", false, false));
        lines.Add((new string('-', 32), false, false));
        foreach (var item in sale.Items)
        {
            lines.Add((item.ProductName, false, false));
            lines.Add(($"  {item.Quantity:N2} x {item.UnitPrice:N2}     {(item.Total):N2}", false, false));
        }
        lines.Add((new string('-', 32), false, false));
        lines.Add(($"Subtotal: {currency} {sale.Subtotal:N2}", false, false));
        lines.Add(($"Discount: {currency} {sale.Discount:N2}", false, false));
        lines.Add(($"Tax:      {currency} {sale.Tax:N2}", false, false));
        lines.Add(($"TOTAL:    {currency} {sale.GrandTotal:N2}", true, false));
        lines.Add(($"Payment:  {sale.PaymentMethod}", false, false));
        lines.Add(($"Paid:     {currency} {sale.PaidAmount:N2}", false, false));
        if (sale.RemainingAmount > 0) lines.Add(($"Due:      {currency} {sale.RemainingAmount:N2}", false, false));
        else if (sale.PaymentMethod.Equals("Cash", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add(($"Received: {currency} {sale.CashReceivedAmount:N2}", false, false));
            lines.Add(($"Change:   {currency} {Math.Max(0, sale.CashReceivedAmount - sale.GrandTotal):N2}", false, false));
        }
        lines.Add(("Thank you", false, true));

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var y = 8d;
            var dpi = VisualTreeHelper.GetDpi(owner).PixelsPerDip;
            foreach (var line in lines)
            {
                var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal,
                    line.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                var formatted = new FormattedText(line.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, fontSize, Brushes.Black, dpi) { MaxTextWidth = Math.Max(80, width - 16), Trimming = TextTrimming.CharacterEllipsis };
                var x = line.Center ? Math.Max(8, (width - formatted.Width) / 2) : 8;
                drawing.DrawText(formatted, new Point(x, y)); y += Math.Max(lineHeight, formatted.Height);
            }
        }
        dialog.PrintVisual(visual, $"Receipt {sale.InvoiceNumber}");
        return true;
    }
}
