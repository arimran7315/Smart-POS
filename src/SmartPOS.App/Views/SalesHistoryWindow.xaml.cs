using System.Windows;
using System.Globalization;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class SalesHistoryWindow : Window
{
    private readonly SalesHistoryViewModel _viewModel;
    private readonly ReceiptPrinter _receiptPrinter;
    public SalesHistoryWindow(SaleService sales, ReturnsService returns, AppUser user, ReceiptPrinter receiptPrinter)
    {
        InitializeComponent(); Title = $"{SmartPosIdentity.Name} | Sales and returns";
        _receiptPrinter = receiptPrinter;
        _viewModel = new SalesHistoryViewModel(sales, returns, user, new WpfUserConfirmationService(this)); DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }
    private async void Reprint_Click(object sender, RoutedEventArgs e)
    {
        var sale = _viewModel.SelectedSale;
        if (sale is null) { _viewModel.ReportStatus("Select a sale before reprinting its receipt."); return; }
        if (sale.Status == "Voided") { _viewModel.ReportStatus("A voided sale receipt cannot be reprinted."); return; }
        try
        {
            var receipt = await _viewModel.GetReceiptForReprintAsync(sale.Id);
            if (receipt is null) return;
            var settings = receipt.Settings;
            var width = double.TryParse(settings.GetValueOrDefault("ReceiptWidthMm"), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var mm) ? mm : 80;
            var printed = _receiptPrinter.Print(this, receipt.Sale, settings.GetValueOrDefault("StoreName") ?? SmartPosIdentity.Name,
                settings.GetValueOrDefault("StoreAddress"), settings.GetValueOrDefault("StorePhone"), receipt.CashierName,
                settings.GetValueOrDefault("Currency") ?? "", width, receipt.CustomerName, settings.GetValueOrDefault("TaxNumber"),
                settings.GetValueOrDefault("DefaultPrinter"), settings.GetValueOrDefault("ReceiptPrintMode"), settings.GetValueOrDefault("StoreEmail"));
            if (printed)
            {
                if (await _viewModel.RecordReceiptReprintedAsync(sale.Id))
                    _viewModel.ReportStatus($"Receipt reprinted for {receipt.Sale.InvoiceNumber}.");
                else
                    MessageBox.Show("The receipt was sent to the printer, but its activity entry could not be saved. Do not reprint because of this message; check the status for details.",
                        "Receipt sent; activity log failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { _viewModel.ReportStatus(UserErrorHandler.GetMessage(ex, "Reprinting the receipt")); }
    }
}
