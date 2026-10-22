using System.Windows;
using SmartPOS.App.Services;
using SmartPOS.App.ViewModels;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.Views;

public partial class PurchaseWindow : Window
{
    public PurchaseWindow(ProductService products, SupplierService suppliers, PurchaseService purchases, PurchaseReturnsService purchaseReturns, SupplierPaymentService supplierPayments, AppUser user)
    {
        InitializeComponent(); WindowLayout.FitToWorkArea(this); Title = $"{SmartPosIdentity.Name} | Receive purchase";
        DataContext = new PurchaseViewModel(products, suppliers, purchases, purchaseReturns, supplierPayments, user, new WpfUserConfirmationService(this));
        ReceiveTab.Visibility = SmartPOS.Core.Services.Permissions.Has(user.Role, "Purchases.Manage") ? Visibility.Visible : Visibility.Collapsed;
        ReturnTab.Visibility = SmartPOS.Core.Services.Permissions.Has(user.Role, "Purchases.Return") ? Visibility.Visible : Visibility.Collapsed;
        PurchasePaymentsTab.Visibility = SmartPOS.Core.Services.Permissions.Has(user.Role, "Payments.Make") ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await ((PurchaseViewModel)DataContext).InitializeAsync();
    }
}
