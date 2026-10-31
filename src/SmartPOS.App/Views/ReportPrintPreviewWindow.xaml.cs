using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SmartPOS.App.Services;

namespace SmartPOS.App.Views;

public partial class ReportPrintPreviewWindow : Window
{
    public ReportPrintPreviewWindow(FlowDocument document)
    {
        InitializeComponent();
        WindowLayout.FitToWorkArea(this);
        Title = $"{SmartPosIdentity.Name} | Report print preview";
        DocumentReader.Document = document;
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentReader.Document is not IDocumentPaginatorSource source) return;
        try
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() == true)
                dialog.PrintDocument(source.DocumentPaginator, $"{SmartPosIdentity.Name} report");
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(UserErrorHandler.GetMessage(ex, "Printing the report"), "Print failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
