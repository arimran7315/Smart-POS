using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Printing;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Printing;

namespace SmartPOS.App.Services;

public sealed class ReceiptPrinter
{
    private readonly IRawPrinter _rawPrinter;

    public ReceiptPrinter(IRawPrinter rawPrinter) => _rawPrinter = rawPrinter;

    public bool Print(Window owner, Sale sale, string storeName, string? address, string? phone, string cashierName, string currency, double paperWidthMm = 80, string? customerName = null, string? taxNumber = null, string? defaultPrinter = null, string? printMode = null, string? storeEmail = null)
    {
        ArgumentNullException.ThrowIfNull(sale);
        PrintQueue? configuredQueue = null;
        if (!string.IsNullOrWhiteSpace(defaultPrinter))
        {
            configuredQueue = new LocalPrintServer().GetPrintQueues()
                .FirstOrDefault(queue => string.Equals(queue.FullName, defaultPrinter.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(queue.Name, defaultPrinter.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        paperWidthMm = paperWidthMm is >= 57 and <= 59 ? 58 : 80;
        var columns = EscPosReceiptEncoder.ColumnsForRollWidth(paperWidthMm);
        var lines = new List<(string Text, bool Bold, bool Center)>
        {
            (storeName, true, true)
        };
        if (!string.IsNullOrWhiteSpace(address)) lines.Add((address.Trim(), false, true));
        if (!string.IsNullOrWhiteSpace(phone)) lines.Add((phone.Trim(), false, true));
        if (!string.IsNullOrWhiteSpace(storeEmail)) lines.Add((storeEmail.Trim(), false, true));
        if (!string.IsNullOrWhiteSpace(taxNumber)) lines.Add(($"Tax No: {taxNumber.Trim()}", false, true));
        lines.Add((new string('-', columns), false, false));
        lines.Add(($"Invoice: {sale.InvoiceNumber}", false, false));
        lines.Add(($"Date: {sale.SoldAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}", false, false));
        lines.Add(($"Cashier: {cashierName}", false, false));
        if (!string.IsNullOrWhiteSpace(customerName)) lines.Add(($"Customer: {customerName.Trim()}", false, false));
        if (!string.IsNullOrWhiteSpace(sale.Notes)) lines.Add(($"Note: {sale.Notes.Trim()}", false, false));
        lines.Add((new string('-', columns), false, false));
        foreach (var item in sale.Items)
        {
            lines.Add((item.ProductName, false, false));
            lines.Add(($"  {item.Quantity:N2} x {item.UnitPrice:N2}     {(item.Total):N2}", false, false));
        }
        lines.Add((new string('-', columns), false, false));
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

        if (string.Equals(printMode, "EscPos", StringComparison.OrdinalIgnoreCase))
        {
            if (configuredQueue is null)
                throw new InvalidOperationException("ESC/POS printing requires a valid printer name in the DefaultPrinter setting.");
            _rawPrinter.Print(configuredQueue.Name, $"Receipt {sale.InvoiceNumber}",
                EscPosReceiptEncoder.Encode(lines.Select(line => new ReceiptTextLine(line.Text, line.Bold, line.Center)), columns));
            return true;
        }

        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (configuredQueue is not null) dialog.PrintQueue = configuredQueue;
        if (dialog.ShowDialog() != true) return false;
        var configuredWidth = paperWidthMm * 96 / 25.4;
        var reportedWidth = dialog.PrintableAreaWidth;
        var width = double.IsFinite(reportedWidth) && reportedWidth >= 120
            ? Math.Min(reportedWidth, configuredWidth)
            : configuredWidth;
        var fontSize = width < 250 ? 9.5 : 10.5;
        var lineHeight = fontSize * 1.55;

        var dpi = VisualTreeHelper.GetDpi(owner).PixelsPerDip;
        var visual = new DrawingVisual();
        var contentHeight = 8d;
        using (var drawing = visual.RenderOpen())
        {
            var y = contentHeight;
            foreach (var line in lines)
            {
                var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal,
                    line.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                var formatted = new FormattedText(line.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, fontSize, Brushes.Black, dpi) { MaxTextWidth = Math.Max(80, width - 16), Trimming = TextTrimming.CharacterEllipsis };
                var x = line.Center ? Math.Max(8, (width - formatted.Width) / 2) : 8;
                drawing.DrawText(formatted, new Point(x, y)); y += Math.Max(lineHeight, formatted.Height);
            }
            contentHeight = y + 8;
        }
        // PrintVisual relies on the driver's default page size and can clip long receipts.
        // Give this roll receipt its configured width and measured content height.
        var page = new FixedPage { Width = width, Height = contentHeight };
        page.Children.Add(new Image { Width = width, Height = contentHeight, Stretch = Stretch.None, Source = new DrawingImage(visual.Drawing) });
        page.Measure(new Size(width, contentHeight));
        page.Arrange(new Rect(0, 0, width, contentHeight));
        page.UpdateLayout();
        var pageContent = new PageContent();
        ((System.Windows.Markup.IAddChild)pageContent).AddChild(page);
        var document = new FixedDocument();
        document.Pages.Add(pageContent);
        document.DocumentPaginator.PageSize = new Size(width, contentHeight);
        dialog.PrintDocument(document.DocumentPaginator, $"Receipt {sale.InvoiceNumber}");
        return true;
    }

}
