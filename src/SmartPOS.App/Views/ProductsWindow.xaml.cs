using System.Windows;
using SmartPOS.App.ViewModels;
using SmartPOS.Infrastructure.Services;
using SmartPOS.Core.Entities;
using SmartPOS.App.Services;
using System.Globalization;
using Microsoft.Win32;
using System.IO;
using System.Windows.Media.Imaging;

namespace SmartPOS.App.Views;

public partial class ProductsWindow : Window
{
    private readonly ProductsViewModel _viewModel;
    private readonly BarcodeLabelPrinter _barcodePrinter = new();
    public ProductsWindow(ProductService products, InventoryService inventory, CatalogTaxonomyService taxonomy, AppUser user)
    {
        InitializeComponent(); Title = $"{SmartPosIdentity.Name} | Products"; _viewModel = new ProductsViewModel(products, inventory, taxonomy, user.Id, user.Role, new WpfUserConfirmationService(this)); DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(ProductsViewModel.SelectedProduct)) UpdateProductImagePreview(); };
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        ProductGrid.SelectionChanged += (_, _) => UpdateProductImagePreview();
    }
    private async void PrintBarcode_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProduct is not { Id: > 0 } product) { MessageBox.Show("Save or select a product first."); return; }
        if (!int.TryParse(LabelCopiesBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies)) { MessageBox.Show("Enter a valid label count."); return; }
        try
        {
            if (_barcodePrinter.Print(this, product, copies))
                await _viewModel.RecordBarcodeLabelsPrintedAsync(product.Id, copies);
        }
        catch (Exception ex) { MessageBox.Show(UserErrorHandler.GetMessage(ex, "Printing barcode labels"), "Barcode label printing failed", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProduct is null) { MessageBox.Show("Select or create a product first."); return; }
        var picker = new OpenFileDialog
        {
            Title = "Choose product image",
            Filter = "Product images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var sourceInfo = new FileInfo(picker.FileName);
            if (sourceInfo.Length is <= 0 or > 10 * 1024 * 1024)
                throw new InvalidDataException("Choose an image smaller than 10 MB.");
            var decoder = BitmapDecoder.Create(new Uri(picker.FileName), BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidDataException("The selected file does not contain a readable image.");

            var imageDirectory = SmartPosPaths.ProductImagesDirectory;
            Directory.CreateDirectory(imageDirectory);
            var destination = Path.Combine(imageDirectory, $"{Guid.NewGuid():N}{sourceInfo.Extension.ToLowerInvariant()}");
            File.Copy(sourceInfo.FullName, destination);
            _viewModel.SelectedProduct.ImagePath = destination;
            UpdateProductImagePreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException)
        {
            MessageBox.Show(UserErrorHandler.GetMessage(ex, "Loading the product image"), "Product image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedProduct is null) return;
        _viewModel.SelectedProduct.ImagePath = null;
        ProductImagePreview.Source = null;
    }

    private void UpdateProductImagePreview()
    {
        ProductImagePreview.Source = null;
        var path = _viewModel.SelectedProduct?.ImagePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = 320;
            image.EndInit();
            image.Freeze();
            ProductImagePreview.Source = image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException)
        {
            MessageBox.Show(UserErrorHandler.GetMessage(ex, "Displaying the product image"), "Product image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
