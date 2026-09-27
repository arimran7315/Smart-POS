using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class ProductsViewModel : INotifyPropertyChanged
{
    private readonly ProductService _products;
    private readonly InventoryService _inventory;
    private readonly CatalogTaxonomyService _taxonomy;
    private readonly int _userId;
    private readonly string _role;
    private Product? _selectedProduct;
    private string _search = "";
    private string _status = "";
    private string _adjustmentQuantity = "";
    private string _adjustmentReason = "Manual stock adjustment";
    private string _categoryName = "", _brandName = "";
    public ObservableCollection<Product> Items { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Brand> Brands { get; } = [];
    public Product? SelectedProduct { get => _selectedProduct; set { _selectedProduct = value; OnPropertyChanged(); SaveCommand?.Refresh(); DeactivateCommand?.Refresh(); AdjustStockCommand?.Refresh(); } }
    public string Search { get => _search; set { _search = value; OnPropertyChanged(); } }
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public string AdjustmentQuantity { get => _adjustmentQuantity; set { _adjustmentQuantity = value; OnPropertyChanged(); } }
    public string AdjustmentReason { get => _adjustmentReason; set { _adjustmentReason = value; OnPropertyChanged(); } }
    public string CategoryName { get => _categoryName; set { _categoryName = value; OnPropertyChanged(); } }
    public string BrandName { get => _brandName; set { _brandName = value; OnPropertyChanged(); } }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand DeactivateCommand { get; }
    public AsyncCommand AdjustStockCommand { get; }
    public AsyncCommand AddCategoryCommand { get; }
    public AsyncCommand AddBrandCommand { get; }

    public ProductsViewModel(ProductService products, InventoryService inventory, CatalogTaxonomyService taxonomy, int userId, string role)
    {
        _products = products; _inventory = inventory; _taxonomy = taxonomy; _userId = userId; _role = role;
        SearchCommand = new(LoadAsync); NewCommand = new(() => { SelectedProduct = new Product(); Status = "New product. Enter the required fields and save."; return Task.CompletedTask; });
        SaveCommand = new(SaveAsync, () => SelectedProduct is not null); DeactivateCommand = new(DeactivateAsync, () => SelectedProduct is { Id: > 0, IsActive: true });
        AdjustStockCommand = new(AdjustStockAsync, () => SelectedProduct is { Id: > 0 });
        AddCategoryCommand = new(AddCategoryAsync, () => Permissions.Has(_role, "Products.Manage"));
        AddBrandCommand = new(AddBrandAsync, () => Permissions.Has(_role, "Products.Manage"));
    }
    public async Task InitializeAsync()
    {
        foreach (var category in await _taxonomy.CategoriesAsync()) Categories.Add(category);
        foreach (var brand in await _taxonomy.BrandsAsync()) Brands.Add(brand);
        await LoadAsync();
    }
    private async Task LoadAsync()
    {
        try { Items.Clear(); foreach (var product in await _products.SearchAsync(Search.Trim(), _userId)) Items.Add(product); Status = $"Showing {Items.Count} active product(s)."; }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task SaveAsync()
    {
        try { var saved = await _products.SaveAsync(SelectedProduct!, _userId); Status = $"Saved {saved.Name}."; await LoadAsync(); SelectedProduct = Items.FirstOrDefault(x => x.Id == saved.Id); }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task DeactivateAsync()
    {
        try { var id = SelectedProduct!.Id; await _products.DeactivateAsync(id, _userId); Status = "Product deactivated; historical sale records are preserved."; SelectedProduct = null; await LoadAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task AdjustStockAsync()
    {
        if (!decimal.TryParse(AdjustmentQuantity, out var delta) || delta == 0) { Status = "Enter a non-zero stock adjustment."; return; }
        try { var id = SelectedProduct!.Id; await _inventory.AdjustAsync(id, delta, _userId, string.IsNullOrWhiteSpace(AdjustmentReason) ? "Manual adjustment" : AdjustmentReason.Trim()); Status = "Stock updated and movement recorded."; await LoadAsync(); SelectedProduct = Items.FirstOrDefault(x => x.Id == id); AdjustmentQuantity = ""; }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task AddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(CategoryName)) { Status = "Enter a category name."; return; }
        try { var item = await _taxonomy.SaveCategoryAsync(new Category { Name = CategoryName.Trim() }, _userId); Categories.Add(item); if (SelectedProduct is not null) SelectedProduct.CategoryId = item.Id; CategoryName = ""; Status = $"Added category {item.Name}."; }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task AddBrandAsync()
    {
        if (string.IsNullOrWhiteSpace(BrandName)) { Status = "Enter a brand name."; return; }
        try { var item = await _taxonomy.SaveBrandAsync(new Brand { Name = BrandName.Trim() }, _userId); Brands.Add(item); if (SelectedProduct is not null) SelectedProduct.BrandId = item.Id; BrandName = ""; Status = $"Added brand {item.Name}."; }
        catch (Exception ex) { Status = ex.Message; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
