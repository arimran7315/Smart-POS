using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Core.Services;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed record CreditSaleChoice(int Id, string InvoiceNumber, string CustomerName, decimal Balance);

public sealed class BackOfficeViewModel : INotifyPropertyChanged
{
    private readonly CustomerService _customers; private readonly SupplierService _suppliers; private readonly ExpenseService _expenses;
    private readonly PaymentService _payments; private readonly CustomerAccountPaymentService _customerAccountPayments; private readonly SupplierPaymentService _supplierPayments; private readonly BackOfficeQueryService _queries; private readonly AuditService _audit; private readonly int _userId; private readonly string _role;
    private Customer? _selectedCustomer; private Supplier? _selectedSupplier; private CreditSaleChoice? _selectedCredit;
    private string _expenseDescription = "", _expenseCategory = "General", _expenseAmount = "", _expensePaymentMethod = "Cash", _expenseNotes = "", _paymentAmount = "", _paymentMethod = "Cash", _paymentReference = "", _customerAccountPaymentAmount = "", _customerAccountPaymentReference = "", _customerAccountPaymentMethod = "Cash", _supplierPaymentAmount = "", _supplierPaymentReference = "", _supplierPaymentMethod = "Cash", _status = "";
    private DateTime? _expenseDate = DateTime.Today;
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<Expense> RecentExpenses { get; } = [];
    public ObservableCollection<AuditEntry> RecentAuditEntries { get; } = [];
    public ObservableCollection<CreditSaleChoice> CreditSales { get; } = [];
    public IReadOnlyList<string> PaymentMethods { get; } = ["Cash", "Card", "Bank transfer"];
    public Customer? SelectedCustomer { get => _selectedCustomer; set { _selectedCustomer = value; Changed(); SaveCustomerCommand?.Refresh(); ReceiveCustomerAccountPaymentCommand?.Refresh(); } }
    public Supplier? SelectedSupplier { get => _selectedSupplier; set { _selectedSupplier = value; Changed(); SaveSupplierCommand?.Refresh(); RecordSupplierPaymentCommand?.Refresh(); } }
    public CreditSaleChoice? SelectedCredit { get => _selectedCredit; set { _selectedCredit = value; Changed(); ReceivePaymentCommand?.Refresh(); } }
    public string ExpenseDescription { get => _expenseDescription; set { _expenseDescription = value; Changed(); } }
    public string ExpenseCategory { get => _expenseCategory; set { _expenseCategory = value; Changed(); } }
    public string ExpenseAmount { get => _expenseAmount; set { _expenseAmount = value; Changed(); } }
    public string ExpensePaymentMethod { get => _expensePaymentMethod; set { _expensePaymentMethod = value; Changed(); } }
    public string ExpenseNotes { get => _expenseNotes; set { _expenseNotes = value; Changed(); } }
    public DateTime? ExpenseDate { get => _expenseDate; set { _expenseDate = value; Changed(); } }
    public string PaymentAmount { get => _paymentAmount; set { _paymentAmount = value; Changed(); } }
    public string PaymentMethod { get => _paymentMethod; set { _paymentMethod = value; Changed(); } }
    public string PaymentReference { get => _paymentReference; set { _paymentReference = value; Changed(); } }
    public string SupplierPaymentAmount { get => _supplierPaymentAmount; set { _supplierPaymentAmount = value; Changed(); RecordSupplierPaymentCommand?.Refresh(); } }
    public string SupplierPaymentReference { get => _supplierPaymentReference; set { _supplierPaymentReference = value; Changed(); } }
    public string SupplierPaymentMethod { get => _supplierPaymentMethod; set { _supplierPaymentMethod = value; Changed(); } }
    public string CustomerAccountPaymentAmount { get => _customerAccountPaymentAmount; set { _customerAccountPaymentAmount = value; Changed(); ReceiveCustomerAccountPaymentCommand?.Refresh(); } }
    public string CustomerAccountPaymentReference { get => _customerAccountPaymentReference; set { _customerAccountPaymentReference = value; Changed(); } }
    public string CustomerAccountPaymentMethod { get => _customerAccountPaymentMethod; set { _customerAccountPaymentMethod = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public AsyncCommand NewCustomerCommand { get; }
    public AsyncCommand SaveCustomerCommand { get; }
    public AsyncCommand NewSupplierCommand { get; }
    public AsyncCommand SaveSupplierCommand { get; }
    public AsyncCommand RecordExpenseCommand { get; }
    public AsyncCommand ReceivePaymentCommand { get; }
    public AsyncCommand RecordSupplierPaymentCommand { get; }
    public AsyncCommand ReceiveCustomerAccountPaymentCommand { get; }
    public AsyncCommand RefreshCommand { get; }

    public BackOfficeViewModel(CustomerService customers, SupplierService suppliers, ExpenseService expenses, PaymentService payments, CustomerAccountPaymentService customerAccountPayments, SupplierPaymentService supplierPayments, BackOfficeQueryService queries, AuditService audit, AppUser user)
    {
        _customers = customers; _suppliers = suppliers; _expenses = expenses; _payments = payments; _customerAccountPayments = customerAccountPayments; _supplierPayments = supplierPayments; _queries = queries; _audit = audit; _userId = user.Id; _role = user.Role;
        NewCustomerCommand = new(() => { SelectedCustomer = new Customer(); Status = "Enter customer details, then save."; return Task.CompletedTask; }, () => Permissions.Has(_role, "Customers.Manage"));
        SaveCustomerCommand = new(SaveCustomerAsync, () => SelectedCustomer is not null && Permissions.Has(_role, "Customers.Manage"));
        NewSupplierCommand = new(() => { SelectedSupplier = new Supplier(); Status = "Enter supplier details, then save."; return Task.CompletedTask; }, () => Permissions.Has(_role, "Suppliers.Manage"));
        SaveSupplierCommand = new(SaveSupplierAsync, () => SelectedSupplier is not null && Permissions.Has(_role, "Suppliers.Manage"));
        RecordExpenseCommand = new(RecordExpenseAsync, () => Permissions.Has(_role, "Expenses.Manage"));
        ReceivePaymentCommand = new(ReceivePaymentAsync, () => SelectedCredit is not null && Permissions.Has(_role, "Payments.Receive"));
        RecordSupplierPaymentCommand = new(RecordSupplierPaymentAsync, () => SelectedSupplier is { Id: > 0, IsActive: true } && decimal.TryParse(SupplierPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) && amount > 0 && Permissions.Has(_role, "Payments.Make"));
        ReceiveCustomerAccountPaymentCommand = new(ReceiveCustomerAccountPaymentAsync, () => SelectedCustomer is { Id: > 0, IsActive: true } && decimal.TryParse(CustomerAccountPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) && amount > 0 && Permissions.Has(_role, "Payments.Receive"));
        RefreshCommand = new(LoadAsync);
    }
    public async Task InitializeAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        try
        {
            Customers.Clear();
            if (Permissions.Has(_role, "Customers.View") || Permissions.Has(_role, "Customers.Manage")) foreach (var item in await _customers.SearchAsync("", _userId)) Customers.Add(item);
            Suppliers.Clear();
            if (Permissions.Has(_role, "Suppliers.Manage")) foreach (var item in await _suppliers.SearchAsync("", _userId)) Suppliers.Add(item);
            RecentExpenses.Clear();
            if (Permissions.Has(_role, "Expenses.Manage")) foreach (var item in await _queries.GetRecentExpensesAsync(_userId)) RecentExpenses.Add(item);
            RecentAuditEntries.Clear();
            if (Permissions.Has(_role, "Audit.View")) foreach (var entry in await _audit.RecentAsync(_userId)) RecentAuditEntries.Add(entry);
            var sales = Permissions.Has(_role, "Payments.Receive")
                ? await _queries.GetOpenCreditSalesAsync(_userId)
                : Array.Empty<OpenCreditSale>();
            CreditSales.Clear(); foreach (var sale in sales) CreditSales.Add(new(sale.Id, sale.InvoiceNumber, sale.CustomerName, sale.Balance));
            Status = "Lists refreshed from the local database.";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading business records"); }
    }
    private async Task SaveCustomerAsync()
    {
        try { var saved = await _customers.SaveAsync(SelectedCustomer!, _userId); Status = $"Saved customer {saved.Name}."; await LoadAsync(); SelectedCustomer = Customers.FirstOrDefault(x => x.Id == saved.Id); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Saving the customer"); }
    }
    private async Task SaveSupplierAsync()
    {
        try { var saved = await _suppliers.SaveAsync(SelectedSupplier!, _userId); Status = $"Saved supplier {saved.Name}."; await LoadAsync(); SelectedSupplier = Suppliers.FirstOrDefault(x => x.Id == saved.Id); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Saving the supplier"); }
    }
    private async Task RecordExpenseAsync()
    {
        if (!decimal.TryParse(ExpenseAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)) { Status = "Enter a valid expense amount."; return; }
        if (ExpenseDate is null) { Status = "Choose the date the expense occurred."; return; }
        try
        {
            var date = DateOnly.FromDateTime(ExpenseDate.Value);
            await _expenses.RecordAsync(ExpenseDescription, ExpenseCategory, amount, _userId,
                paymentMethod: ExpensePaymentMethod, notes: ExpenseNotes, spentOn: date);
            ExpenseDescription = ""; ExpenseAmount = ""; ExpenseNotes = ""; ExpenseDate = DateTime.Today;
            Status = "Expense recorded."; await LoadAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Recording the expense"); }
    }
    private async Task ReceivePaymentAsync()
    {
        if (!decimal.TryParse(PaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)) { Status = "Enter a valid payment amount."; return; }
        try { await _payments.ReceiveCreditPaymentAsync(SelectedCredit!.Id, amount, _userId, PaymentMethod, PaymentReference); PaymentAmount = ""; PaymentReference = ""; Status = "Credit payment recorded."; await LoadAsync(); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Recording the customer payment"); }
    }
    private async Task RecordSupplierPaymentAsync()
    {
        if (SelectedSupplier is not { Id: > 0 } supplier || !decimal.TryParse(SupplierPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        { Status = "Select an active supplier and enter a positive payment amount."; return; }
        try
        {
            await _supplierPayments.RecordAsync(supplier.Id, amount, _userId, SupplierPaymentMethod, SupplierPaymentReference);
            SupplierPaymentAmount = ""; SupplierPaymentReference = ""; Status = $"Payment recorded for {supplier.Name}.";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Recording the supplier payment"); }
    }
    private async Task ReceiveCustomerAccountPaymentAsync()
    {
        if (SelectedCustomer is not { Id: > 0 } customer || !decimal.TryParse(CustomerAccountPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        { Status = "Select an active customer and enter a positive payment amount."; return; }
        try
        {
            await _customerAccountPayments.ReceiveAsync(customer.Id, amount, _userId, CustomerAccountPaymentMethod, CustomerAccountPaymentReference);
            CustomerAccountPaymentAmount = ""; CustomerAccountPaymentReference = ""; Status = $"Account payment recorded for {customer.Name}.";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Recording the customer account payment"); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
