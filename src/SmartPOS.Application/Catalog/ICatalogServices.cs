using SmartPOS.Core.Entities;

namespace SmartPOS.UseCases.Catalog;

public interface IProductCatalogService
{
    Task RecordBarcodeLabelsPrintedAsync(int productId, int copies, int userId, CancellationToken ct = default);
    Task<Product> SaveAsync(Product product, int userId, CancellationToken ct = default);
    Task DeactivateAsync(int productId, int userId, CancellationToken ct = default);
    Task<List<Product>> SearchAsync(string query, int userId, int skip = 0, int take = 50,
        string? categoryFilter = null, bool lowStockOnly = false, string sortBy = "Name", CancellationToken ct = default);
}

public interface IInventoryManagementService
{
    Task AdjustAsync(int productId, decimal delta, int userId, string reason, CancellationToken ct = default);
}

public interface ICatalogTaxonomyService
{
    Task<Category> SaveCategoryAsync(Category category, int userId, CancellationToken ct = default);
    Task<Brand> SaveBrandAsync(Brand brand, int userId, CancellationToken ct = default);
    Task DeactivateCategoryAsync(int categoryId, int userId, CancellationToken ct = default);
    Task DeactivateBrandAsync(int brandId, int userId, CancellationToken ct = default);
    Task<List<Category>> CategoriesAsync(int userId, CancellationToken ct = default);
    Task<List<Brand>> BrandsAsync(int userId, CancellationToken ct = default);
}
