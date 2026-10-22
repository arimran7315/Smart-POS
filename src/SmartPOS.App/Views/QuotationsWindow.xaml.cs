using System.Windows;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class QuotationsWindow : Window
{
    private readonly QuotationsViewModel _viewModel;
    public QuotationsWindow(QuotationService quotes, ProductService products, CustomerService customers, SaleService sales, AppUser user)
    {
        InitializeComponent(); WindowLayout.FitToWorkArea(this); Title = $"{SmartPosIdentity.Name} | Quotations";
        _viewModel = new QuotationsViewModel(quotes, products, customers, sales, user, new WpfUserConfirmationService(this));
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }
}
