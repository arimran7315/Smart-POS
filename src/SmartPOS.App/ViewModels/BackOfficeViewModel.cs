using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Data;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed record CreditSaleChoice(int Id, string InvoiceNumber, string CustomerName, decimal Balance);

public sealed class BackOfficeViewModel : INotifyPropertyChanged
{
    private readonly CustomerService _customers; private readonly SupplierService _suppliers; private readonly ExpenseService _expenses;
    private readonly PaymentService _payments; private readonly PosDbContext _db; private readonly int _userId; private readonly string _role;
    private Customer? _selectedCustomer; private Supplier? _selectedSupplier; private CreditSaleChoice? _selectedCredit;
    private string _expenseDescription = "", _expenseCategory = "General", _expenseAmount = "", _paymentAmount = "", _paymentMethod = "Cash", _paymentReference = "", _status = "";
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<Expense> RecentExpenses { get; } = [];
    public ObservableCollection<CreditSaleChoice> CreditSales { get; } = [];
    public IReadOnlyList<string> PaymentMethods { get; } = ["Cash", "Card", "Bank transfer"];
    public Customer? SelectedCustomer { get => _selectedCustomer; set { _selectedCustomer = value; Changed(); SaveCustomerCommand?.Refresh(); } }
    public Supplier? SelectedSupplier { get => _selectedSupplier; set { _selectedSupplier = value; Changed(); SaveSupplierCommand?.Refresh(); } }
    public CreditSaleChoice? SelectedCredit { get => _selectedCredit; set { _selectedCredit = value; Changed(); ReceivePaymentCommand?.Refresh(); } }
    public string ExpenseDescription { get => _expenseDescription; set { _expenseDescription = value; Changed(); } }
    public string ExpenseCategory { get => _expenseCategory; set { _expenseCategory = value; Changed(); } }
    public string ExpenseAmount { get => _expenseAmount; set { _expenseAmount = value; Changed(); } }
    public string PaymentAmount { get => _paymentAmount; set { _paymentAmount = value; Changed(); } }
    public string PaymentMethod { get => _paymentMethod; set { _paymentMethod = value; Changed(); } }
    public string PaymentReference { get => _paymentReference; set { _paymentReference = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public AsyncCommand NewCustomerCommand { get; }
    public AsyncCommand SaveCustomerCommand { get; }
    public AsyncCommand NewSupplierCommand { get; }
    public AsyncCommand SaveSupplierCommand { get; }
    public AsyncCommand RecordExpenseCommand { get; }
    public AsyncCommand ReceivePaymentCommand { get; }
    public AsyncCommand RefreshCommand { get; }

    public BackOfficeViewModel(CustomerService customers, SupplierService suppliers, ExpenseService expenses, PaymentService payments, PosDbContext db, AppUser user)
    {
        _customers = customers; _suppliers = suppliers; _expenses = expenses; _payments = payments; _db = db; _userId = user.Id; _role = user.Role;
        NewCustomerCommand = new(() => { SelectedCustomer = new Customer(); Status = "Enter customer details, then save."; return Task.CompletedTask; }, () => Permissions.Has(_role, "Customers.Manage"));
        SaveCustomerCommand = new(SaveCustomerAsync, () => SelectedCustomer is not null && Permissions.Has(_role, "Customers.Manage"));
        NewSupplierCommand = new(() => { SelectedSupplier = new Supplier(); Status = "Enter supplier details, then save."; return Task.CompletedTask; }, () => Permissions.Has(_role, "Suppliers.Manage"));
        SaveSupplierCommand = new(SaveSupplierAsync, () => SelectedSupplier is not null && Permissions.Has(_role, "Suppliers.Manage"));
        RecordExpenseCommand = new(RecordExpenseAsync, () => Permissions.Has(_role, "Expenses.Manage"));
        ReceivePaymentCommand = new(ReceivePaymentAsync, () => SelectedCredit is not null && Permissions.Has(_role, "Payments.Receive"));
        RefreshCommand = new(LoadAsync);
    }
    public async Task InitializeAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        try
        {
            Customers.Clear();
            if (Permissions.Has(_role, "Customers.Manage")) foreach (var item in await _customers.SearchAsync("", _userId)) Customers.Add(item);
            Suppliers.Clear();
            if (Permissions.Has(_role, "Suppliers.Manage")) foreach (var item in await _suppliers.SearchAsync("", _userId)) Suppliers.Add(item);
            RecentExpenses.Clear();
            if (Permissions.Has(_role, "Expenses.Manage")) foreach (var item in await _db.Expenses.AsNoTracking().OrderByDescending(x => x.SpentAtUtc).Take(25).ToListAsync()) RecentExpenses.Add(item);
            var sales = Permissions.Has(_role, "Payments.Receive")
                ? await _db.Sales.AsNoTracking().Where(x => x.RemainingAmount > 0 && x.Status != "Voided").OrderByDescending(x => x.SoldAtUtc).ToListAsync()
                : new List<Sale>();
            var customerIds = sales.Where(x => x.CustomerId.HasValue).Select(x => x.CustomerId!.Value).Distinct().ToArray();
            var names = await _db.Customers.AsNoTracking().Where(x => customerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name);
            CreditSales.Clear(); foreach (var sale in sales) CreditSales.Add(new(sale.Id, sale.InvoiceNumber,
                sale.CustomerId is int id && names.TryGetValue(id, out var name) ? name : "Unknown customer", sale.RemainingAmount));
            Status = "Lists refreshed from the local database.";
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task SaveCustomerAsync()
    {
        try { var saved = await _customers.SaveAsync(SelectedCustomer!, _userId); Status = $"Saved customer {saved.Name}."; await LoadAsync(); SelectedCustomer = Customers.FirstOrDefault(x => x.Id == saved.Id); }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task SaveSupplierAsync()
    {
        try { var saved = await _suppliers.SaveAsync(SelectedSupplier!, _userId); Status = $"Saved supplier {saved.Name}."; await LoadAsync(); SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == saved.Id); }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task RecordExpenseAsync()
    {
        if (!decimal.TryParse(ExpenseAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)) { Status = "Enter a valid expense amount."; return; }
        try { await _expenses.RecordAsync(ExpenseDescription, ExpenseCategory, amount, _userId); ExpenseDescription = ""; ExpenseAmount = ""; Status = "Expense recorded."; await LoadAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    }
    private async Task ReceivePaymentAsync()
    {
        if (!decimal.TryParse(PaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)) { Status = "Enter a valid payment amount."; return; }
        try { await _payments.ReceiveCreditPaymentAsync(SelectedCredit!.Id, amount, _userId, PaymentMethod, PaymentReference); PaymentAmount = ""; PaymentReference = ""; Status = "Credit payment recorded."; await LoadAsync(); }
        catch (Exception ex) { Status = ex.Message; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
