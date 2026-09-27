using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartPOS.Core.Entities;
using ZXing;
using ZXing.Common;

namespace SmartPOS.App.Services;

public sealed class BarcodeLabelPrinter
{
    public bool Print(Window owner, Product product, int copies, double widthMm = 50, double heightMm = 30)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (copies is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(copies), "Print between 1 and 500 labels at a time.");
        var value = string.IsNullOrWhiteSpace(product.Barcode) ? product.Sku : product.Barcode.Trim();
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Assign a barcode or SKU before printing labels.");
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = 440, Height = 150, Margin = 8, PureBarcode = true }
        };
        var pixels = writer.Write(value);
        var barcode = new WriteableBitmap(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null);
        barcode.WritePixels(new Int32Rect(0, 0, pixels.Width, pixels.Height), pixels.Pixels, pixels.Width * 4, 0);
        barcode.Freeze();

        var width = widthMm * 96 / 25.4; var height = heightMm * 96 / 25.4;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var title = new FormattedText(product.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 9, Brushes.Black, 1) { MaxTextWidth = width - 8, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(title, new Point(4, 2));
            var codeHeight = height * .60;
            dc.DrawImage(barcode, new Rect(4, 18, width - 8, codeHeight));
            var code = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 8, Brushes.Black, 1);
            dc.DrawText(code, new Point(Math.Max(4, (width - code.Width) / 2), 20 + codeHeight));
            var price = new FormattedText(product.SalePrice.ToString("N2", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 8, Brushes.Black, 1);
            dc.DrawText(price, new Point(4, height - price.Height - 2));
        }
        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true) return false;
        dialog.PrintTicket.CopyCount = copies;
        dialog.PrintVisual(visual, $"Barcode labels: {product.Sku}");
        return true;
    }
}
