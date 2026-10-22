using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;
using SmartPOS.UseCases.Catalog;

namespace SmartPOS.App.ViewModels;

public sealed class ProductsViewModel : INotifyPropertyChanged
{
    private readonly IProductCatalogService _products;
    private readonly IInventoryManagementService _inventory;
    private readonly ICatalogTaxonomyService _taxonomy;
    private readonly IUserConfirmationService _confirmation;
    private readonly int _userId;
    private readonly string _role;
    private Product? _selectedProduct;
    private string _search = "";
    private string _categoryFilter = "";
    private bool _lowStockOnly;
    private string _sortBy = "Name";
    private string _status = "";
    private string _adjustmentQuantity = "";
    private string _adjustmentReason = "Manual stock adjustment";
    private string _categoryName = "", _brandName = "";
    private const int PageSize = 50;
    private int _pageIndex;
    private bool _hasNextPage;
    private bool _hasPreviousPage;
    private string _pageSummary = "Page 1";
    private Category? _selectedCategory;
    private Brand? _selectedBrand;
    public ObservableCollection<Product> Items { get; } = [];
    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Brand> Brands { get; } = [];
    public ObservableCollection<string> SortOptions { get; } = ["Name", "SKU", "Sale price", "Stock"];
    public Category? SelectedCategory { get => _selectedCategory; set { _selectedCategory = value; if (value is not null) _categoryName = value.Name; OnPropertyChanged(); OnPropertyChanged(nameof(CategoryName)); SaveCategoryCommand?.Refresh(); DeactivateCategoryCommand?.Refresh(); } }
    public Brand? SelectedBrand { get => _selectedBrand; set { _selectedBrand = value; if (value is not null) _brandName = value.Name; OnPropertyChanged(); OnPropertyChanged(nameof(BrandName)); SaveBrandCommand?.Refresh(); DeactivateBrandCommand?.Refresh(); } }
    public Product? SelectedProduct { get => _selectedProduct; set { _selectedProduct = value; OnPropertyChanged(); SaveCommand?.Refresh(); DeactivateCommand?.Refresh(); AdjustStockCommand?.Refresh(); } }
    public string Search { get => _search; set { _search = value; OnPropertyChanged(); } }
    public string CategoryFilter { get => _categoryFilter; set { _categoryFilter = value; OnPropertyChanged(); } }
    public bool LowStockOnly { get => _lowStockOnly; set { _lowStockOnly = value; OnPropertyChanged(); } }
    public string SortBy { get => _sortBy; set { _sortBy = value; OnPropertyChanged(); } }
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }
    public bool HasNextPage { get => _hasNextPage; private set { _hasNextPage = value; OnPropertyChanged(); NextPageCommand?.Refresh(); } }
    public bool HasPreviousPage { get => _hasPreviousPage; private set { _hasPreviousPage = value; OnPropertyChanged(); PreviousPageCommand?.Refresh(); } }
    public string PageSummary { get => _pageSummary; private set { _pageSummary = value; OnPropertyChanged(); } }
    public string AdjustmentQuantity { get => _adjustmentQuantity; set { _adjustmentQuantity = value; OnPropertyChanged(); } }
    public string AdjustmentReason { get => _adjustmentReason; set { _adjustmentReason = value; OnPropertyChanged(); } }
    public string CategoryName { get => _categoryName; set { _categoryName = value; OnPropertyChanged(); SaveCategoryCommand?.Refresh(); } }
    public string BrandName { get => _brandName; set { _brandName = value; OnPropertyChanged(); SaveBrandCommand?.Refresh(); } }
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand PreviousPageCommand { get; }
    public AsyncCommand NextPageCommand { get; }
    public AsyncCommand NewCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand DeactivateCommand { get; }
    public AsyncCommand AdjustStockCommand { get; }
    public AsyncCommand AddCategoryCommand { get; }
    public AsyncCommand AddBrandCommand { get; }
    public AsyncCommand SaveCategoryCommand { get; }
    public AsyncCommand SaveBrandCommand { get; }
    public AsyncCommand DeactivateCategoryCommand { get; }
    public AsyncCommand DeactivateBrandCommand { get; }
    public AsyncCommand NewCategoryCommand { get; }
    public AsyncCommand NewBrandCommand { get; }

    public ProductsViewModel(IProductCatalogService products, IInventoryManagementService inventory, ICatalogTaxonomyService taxonomy, int userId, string role, IUserConfirmationService confirmation)
    {
        _products = products; _inventory = inventory; _taxonomy = taxonomy; _userId = userId; _role = role; _confirmation = confirmation;
        SearchCommand = new(async () => { _pageIndex = 0; await LoadAsync(); });
        PreviousPageCommand = new(async () => { if (_pageIndex > 0) { _pageIndex--; await LoadAsync(); } }, () => HasPreviousPage);
        NextPageCommand = new(async () => { if (HasNextPage) { _pageIndex++; await LoadAsync(); } }, () => HasNextPage);
        NewCommand = new(() => { SelectedProduct = new Product(); Status = "New product. Enter the required fields and save."; return Task.CompletedTask; });
        SaveCommand = new(SaveAsync, () => SelectedProduct is not null); DeactivateCommand = new(DeactivateAsync,
            () => SelectedProduct is { Id: > 0, IsActive: true } && Permissions.Has(_role, "Products.Manage"));
        AdjustStockCommand = new(AdjustStockAsync,
            () => SelectedProduct is { Id: > 0 } && Permissions.Has(_role, "Inventory.Manage"));
        AddCategoryCommand = new(AddCategoryAsync, () => Permissions.Has(_role, "Products.Manage"));
        AddBrandCommand = new(AddBrandAsync, () => Permissions.Has(_role, "Products.Manage"));
        SaveCategoryCommand = new(AddCategoryAsync, () => Permissions.Has(_role, "Products.Manage") && !string.IsNullOrWhiteSpace(CategoryName));
        SaveBrandCommand = new(AddBrandAsync, () => Permissions.Has(_role, "Products.Manage") && !string.IsNullOrWhiteSpace(BrandName));
        DeactivateCategoryCommand = new(DeactivateCategoryAsync, () => SelectedCategory is not null && Permissions.Has(_role, "Products.Manage"));
        DeactivateBrandCommand = new(DeactivateBrandAsync, () => SelectedBrand is not null && Permissions.Has(_role, "Products.Manage"));
        NewCategoryCommand = new(() => { SelectedCategory = null; CategoryName = ""; return Task.CompletedTask; }, () => Permissions.Has(_role, "Products.Manage"));
        NewBrandCommand = new(() => { SelectedBrand = null; BrandName = ""; return Task.CompletedTask; }, () => Permissions.Has(_role, "Products.Manage"));
    }
    public async Task InitializeAsync()
    {
        foreach (var category in await _taxonomy.CategoriesAsync(_userId)) Categories.Add(category);
        foreach (var brand in await _taxonomy.BrandsAsync(_userId)) Brands.Add(brand);
        await LoadAsync();
    }
    private async Task LoadAsync()
    {
        try
        {
            var page = await _products.SearchAsync(Search.Trim(), _userId, _pageIndex * PageSize, PageSize + 1,
                categoryFilter: CategoryFilter, lowStockOnly: LowStockOnly, sortBy: SortBy);
            if (page.Count == 0 && _pageIndex > 0) { _pageIndex--; await LoadAsync(); return; }
            HasNextPage = page.Count > PageSize;
            HasPreviousPage = _pageIndex > 0;
            PageSummary = $"Page {_pageIndex + 1}";
            Items.Clear();
            foreach (var product in page.Take(PageSize)) Items.Add(product);
            Status = $"Showing {Items.Count} product(s) on this page.";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading products"); }
    }
    private async Task SaveAsync()
    {
        try { var isNew = SelectedProduct!.Id == 0; var saved = await _products.SaveAsync(SelectedProduct, _userId); Status = $"Saved {saved.Name}."; if (isNew) _pageIndex = 0; await LoadAsync(); SelectedProduct = Items.FirstOrDefault(x => x.Id == saved.Id); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Saving the product"); }
    }
    private async Task DeactivateAsync()
    {
        var product = SelectedProduct;
        if (product is null) return;
        if (!await _confirmation.ConfirmAsync("Confirm deactivation",
                $"Deactivate {product.Name}? Historical sales will remain unchanged.", ConfirmationKind.Warning)) return;
        try { var id = product.Id; await _products.DeactivateAsync(id, _userId); Status = "Product deactivated; historical sale records are preserved."; SelectedProduct = null; await LoadAsync(); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Deactivating the product"); }
    }
    public async Task RecordBarcodeLabelsPrintedAsync(int productId, int copies)
    {
        try
        {
            await _products.RecordBarcodeLabelsPrintedAsync(productId, copies, _userId);
            Status = $"Printed {copies} barcode label(s).";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Recording printed barcode labels"); }
    }
    private async Task AdjustStockAsync()
    {
        if (!decimal.TryParse(AdjustmentQuantity, out var delta) || delta == 0) { Status = "Enter a non-zero stock adjustment."; return; }
        var product = SelectedProduct;
        if (product is null) return;
        var reason = string.IsNullOrWhiteSpace(AdjustmentReason) ? "Manual adjustment" : AdjustmentReason.Trim();
        if (!await _confirmation.ConfirmAsync("Confirm stock adjustment",
                $"Adjust {product.Name} stock by {delta:+0.###;-0.###} unit(s)? Reason: {reason}", ConfirmationKind.Warning)) return;
        try { var id = product.Id; await _inventory.AdjustAsync(id, delta, _userId, reason); Status = "Stock updated and movement recorded."; await LoadAsync(); SelectedProduct = Items.FirstOrDefault(x => x.Id == id); AdjustmentQuantity = ""; }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Adjusting inventory"); }
    }
    private async Task AddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(CategoryName)) { Status = "Enter a category name."; return; }
        try { var item = await _taxonomy.SaveCategoryAsync(SelectedCategory is null ? new Category { Name = CategoryName.Trim() } : new Category { Id = SelectedCategory.Id, Name = CategoryName.Trim(), CreatedAtUtc = SelectedCategory.CreatedAtUtc, IsActive = SelectedCategory.IsActive }, _userId); Replace(Categories, item, x => x.Id); if (SelectedProduct is not null) SelectedProduct.CategoryId = item.Id; SelectedCategory = item; CategoryName = item.Name; Status = $"Saved category {item.Name}."; }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Adding the category"); }
    }
    private async Task AddBrandAsync()
    {
        if (string.IsNullOrWhiteSpace(BrandName)) { Status = "Enter a brand name."; return; }
        try { var item = await _taxonomy.SaveBrandAsync(SelectedBrand is null ? new Brand { Name = BrandName.Trim() } : new Brand { Id = SelectedBrand.Id, Name = BrandName.Trim(), CreatedAtUtc = SelectedBrand.CreatedAtUtc, IsActive = SelectedBrand.IsActive }, _userId); Replace(Brands, item, x => x.Id); if (SelectedProduct is not null) SelectedProduct.BrandId = item.Id; SelectedBrand = item; BrandName = item.Name; Status = $"Saved brand {item.Name}."; }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Adding the brand"); }
    }
    private async Task DeactivateCategoryAsync()
    {
        var category = SelectedCategory;
        if (category is null) return;
        if (!await _confirmation.ConfirmAsync("Confirm deactivation",
                $"Deactivate category {category.Name}? Existing products will keep their historical category link.", ConfirmationKind.Warning)) return;
        try { await _taxonomy.DeactivateCategoryAsync(category.Id, _userId); Categories.Remove(category); SelectedCategory = null; CategoryName = ""; Status = "Category deactivated; existing products retain their category link."; }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Deactivating the category"); }
    }
    private async Task DeactivateBrandAsync()
    {
        var brand = SelectedBrand;
        if (brand is null) return;
        if (!await _confirmation.ConfirmAsync("Confirm deactivation",
                $"Deactivate brand {brand.Name}? Existing products will keep their historical brand link.", ConfirmationKind.Warning)) return;
        try { await _taxonomy.DeactivateBrandAsync(brand.Id, _userId); Brands.Remove(brand); SelectedBrand = null; BrandName = ""; Status = "Brand deactivated; existing products retain their brand link."; }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Deactivating the brand"); }
    }
    private static void Replace<T>(ObservableCollection<T> items, T item, Func<T, int> key)
    {
        var index = items.ToList().FindIndex(x => key(x) == key(item));
        if (index < 0) items.Add(item); else items[index] = item;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
