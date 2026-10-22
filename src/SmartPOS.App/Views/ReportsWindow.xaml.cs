using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class ReportsWindow : Window
{
    private readonly ReportsViewModel _viewModel;
    public ReportsWindow(ReportsService reports, AppUser user)
    {
        InitializeComponent(); WindowLayout.FitToWorkArea(this); Title = $"{SmartPosIdentity.Name} | Reports";
        _viewModel = new ReportsViewModel(reports, user); DataContext = _viewModel;
        if (!Permissions.Has(user.Role, "Reports.Profit")) ProfitTab.Visibility = Visibility.Collapsed;
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var grid = GetSelectedReportGrid();
        if (grid is null)
        { MessageBox.Show("Select a report table before exporting.", "Export report", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var rows = grid.Items.Cast<object>().ToArray();
        if (rows.Length == 0) { MessageBox.Show("There are no report rows to export.", "Export report", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var properties = rows[0].GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(x => x.CanRead).ToArray();
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true, FileName = $"smartpos-report-{DateTime.Today:yyyy-MM-dd}.csv" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(',', properties.Select(x => CsvCellEncoder.Encode(x.Name, isUntrustedText: false))));
            foreach (var row in rows)
                csv.AppendLine(string.Join(',', properties.Select(property => CsvCellEncoder.Encode(
                    Convert.ToString(property.GetValue(row), CultureInfo.InvariantCulture) ?? "", property.PropertyType == typeof(string)))));
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) { MessageBox.Show(UserErrorHandler.GetMessage(ex, "Exporting the report"), "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void PrintPreview_Click(object sender, RoutedEventArgs e)
    {
        var grid = GetSelectedReportGrid();
        if (grid is null)
        { MessageBox.Show("Select a report table before opening print preview.", "Print preview", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var rows = grid.Items.Cast<object>().ToArray();
        if (rows.Length == 0)
        { MessageBox.Show("There are no report rows to print.", "Print preview", MessageBoxButton.OK, MessageBoxImage.Information); return; }

        try
        {
            var columns = grid.Columns.Where(x => x.Visibility == Visibility.Visible).OrderBy(x => x.DisplayIndex)
                .Select(column => (Header: Convert.ToString(column.Header, CultureInfo.CurrentCulture) ?? "", Property: GetBoundProperty(column, rows[0].GetType())))
                .Where(x => x.Property is not null).ToArray();
            if (columns.Length == 0) throw new InvalidOperationException("The selected report has no printable columns.");
            var reportName = ReportTabs.SelectedItem is TabItem tab
                ? Convert.ToString(tab.Header, CultureInfo.CurrentCulture) ?? "Report" : "Report";
            var document = BuildPrintDocument(SmartPosIdentity.Name, reportName, _viewModel.From, _viewModel.Through, rows, columns);
            new ReportPrintPreviewWindow(document) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(UserErrorHandler.GetMessage(ex, "Preparing the report preview"), "Print preview failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private DataGrid? GetSelectedReportGrid()
    {
        if (ReportTabs.SelectedContent is DataGrid grid) return grid;
        return ReportTabs.SelectedContent is DependencyObject content ? FindDataGrid(content) : null;
    }

    private static DataGrid? FindDataGrid(DependencyObject root)
    {
        if (root is DataGrid grid) return grid;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindDataGrid(VisualTreeHelper.GetChild(root, index));
            if (found is not null) return found;
        }
        return null;
    }

    private static PropertyInfo? GetBoundProperty(DataGridColumn column, Type rowType)
    {
        if (column is DataGridBoundColumn { Binding: Binding binding } && !string.IsNullOrWhiteSpace(binding.Path?.Path))
            return rowType.GetProperty(binding.Path.Path.Split('.').Last(), BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        return null;
    }

    private static FlowDocument BuildPrintDocument(string applicationName, string reportName, string from, string through, IReadOnlyList<object> rows,
        IReadOnlyList<(string Header, PropertyInfo? Property)> columns)
    {
        var landscape = columns.Count > 5;
        var document = new FlowDocument
        {
            PageWidth = landscape ? 1122 : 793.7,
            PageHeight = landscape ? 793.7 : 1122,
            PagePadding = new Thickness(36),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 9,
            Foreground = System.Windows.Media.Brushes.Black,
            Background = System.Windows.Media.Brushes.White
        };
        document.Blocks.Add(new Paragraph(new Run($"{applicationName} · {reportName}"))
        {
            FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var period = reportName is "Inventory valuation" or "Low stock"
            ? $"Snapshot: {DateTime.Now:g}"
            : $"Date range: {from} through {through}     Printed: {DateTime.Now:g}";
        document.Blocks.Add(new Paragraph(new Run(period))
        { FontSize = 9, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 16) });

        var table = new Table { CellSpacing = 0, BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(0.5) };
        foreach (var _ in columns) table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var headerGroup = new TableRowGroup();
        var header = new TableRow();
        foreach (var column in columns) header.Cells.Add(CreateCell(column.Header, bold: true, shade: true));
        headerGroup.Rows.Add(header);
        table.RowGroups.Add(headerGroup);

        var body = new TableRowGroup();
        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var column in columns)
                tableRow.Cells.Add(CreateCell(FormatPrintValue(column.Property!.GetValue(row))));
            body.Rows.Add(tableRow);
        }
        table.RowGroups.Add(body);
        document.Blocks.Add(table);
        return document;
    }

    private static TableCell CreateCell(string value, bool bold = false, bool shade = false)
    {
        var paragraph = new Paragraph(new Run(value)) { Margin = new Thickness(0) };
        if (bold) paragraph.FontWeight = FontWeights.SemiBold;
        return new TableCell(paragraph)
        {
            Padding = new Thickness(4), BorderBrush = System.Windows.Media.Brushes.Gray,
            BorderThickness = new Thickness(0.5), Background = shade ? System.Windows.Media.Brushes.Gainsboro : System.Windows.Media.Brushes.White
        };
    }

    private static string FormatPrintValue(object? value) => value switch
    {
        null => "",
        decimal amount => amount.ToString("N2", CultureInfo.CurrentCulture),
        DateTime dateTime => dateTime.ToString("g", CultureInfo.CurrentCulture),
        DateOnly date => date.ToString("d", CultureInfo.CurrentCulture),
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? ""
    };

}
