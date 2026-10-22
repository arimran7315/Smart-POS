using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SmartPOS.App.Services;
using SmartPOS.Core.Entities;
using SmartPOS.Infrastructure.Services;

namespace SmartPOS.App.ViewModels;

public sealed class ShiftViewModel : INotifyPropertyChanged
{
    private readonly ShiftService _shifts; private readonly IUserConfirmationService _confirmation; private readonly int _userId;
    private Shift? _currentShift; private ShiftSummary? _summary; private ShiftListRow? _selectedShift;
    private string _openingFloat = "0", _countedCash = "", _closeNotes = "", _status = "";

    public ObservableCollection<ShiftListRow> Shifts { get; } = [];
    public Shift? CurrentShift { get => _currentShift; private set { _currentShift = value; Changed(); Changed(nameof(HasOpenShift)); Changed(nameof(NoOpenShift)); OpenShiftCommand?.Refresh(); CloseShiftCommand?.Refresh(); RefreshSummaryCommand?.Refresh(); } }
    public ShiftSummary? Summary { get => _summary; private set { _summary = value; Changed(); } }
    public ShiftListRow? SelectedShift { get => _selectedShift; set { _selectedShift = value; Changed(); ViewSelectedCommand?.Refresh(); } }
    public bool HasOpenShift => CurrentShift is not null;
    public bool NoOpenShift => CurrentShift is null;
    public string OpeningFloat { get => _openingFloat; set { _openingFloat = value; Changed(); } }
    public string CountedCash { get => _countedCash; set { _countedCash = value; Changed(); } }
    public string CloseNotes { get => _closeNotes; set { _closeNotes = value; Changed(); } }
    public string Status { get => _status; private set { _status = value; Changed(); } }

    public AsyncCommand OpenShiftCommand { get; }
    public AsyncCommand CloseShiftCommand { get; }
    public AsyncCommand RefreshSummaryCommand { get; }
    public AsyncCommand RefreshListCommand { get; }
    public AsyncCommand ViewSelectedCommand { get; }

    public ShiftViewModel(ShiftService shifts, AppUser user, IUserConfirmationService confirmation)
    {
        _shifts = shifts; _userId = user.Id; _confirmation = confirmation;
        OpenShiftCommand = new(OpenAsync, () => !HasOpenShift);
        CloseShiftCommand = new(CloseAsync, () => HasOpenShift);
        RefreshSummaryCommand = new(RefreshSummaryAsync, () => HasOpenShift);
        RefreshListCommand = new(LoadListAsync);
        ViewSelectedCommand = new(ViewSelectedAsync, () => SelectedShift is not null);
    }

    public async Task InitializeAsync()
    {
        try
        {
            CurrentShift = await _shifts.GetOpenShiftAsync(_userId);
            if (CurrentShift is not null) Summary = await _shifts.GetSummaryAsync(CurrentShift.Id, _userId);
            else Summary = null;
            await LoadListAsync();
            Status = HasOpenShift ? "An open shift is in progress." : "No open shift. Enter an opening float and open one.";
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading shift information"); }
    }

    private async Task LoadListAsync()
    {
        try { Shifts.Clear(); foreach (var row in await _shifts.SearchAsync(_userId)) Shifts.Add(row); }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading shifts"); }
    }

    private async Task OpenAsync()
    {
        if (!decimal.TryParse(OpeningFloat, NumberStyles.Number, CultureInfo.CurrentCulture, out var opening) || opening < 0) { Status = "Enter a valid opening cash float."; return; }
        try
        {
            var shift = await _shifts.OpenAsync(_userId, opening);
            CurrentShift = shift; Summary = await _shifts.GetSummaryAsync(shift.Id, _userId);
            Status = $"Shift opened with {opening:N2} float."; await LoadListAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Opening the shift"); }
    }

    private async Task RefreshSummaryAsync()
    {
        if (CurrentShift is null) return;
        try { Summary = await _shifts.GetSummaryAsync(CurrentShift.Id, _userId); Status = $"Shift totals refreshed at {DateTime.Now:T} (X report)."; }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Refreshing shift totals"); }
    }

    private async Task CloseAsync()
    {
        if (CurrentShift is null) return;
        if (!decimal.TryParse(CountedCash, NumberStyles.Number, CultureInfo.CurrentCulture, out var counted) || counted < 0) { Status = "Enter the counted cash amount."; return; }
        if (!await _confirmation.ConfirmAsync("Close shift", "Close this shift and record the Z report? You cannot reopen it.", ConfirmationKind.Warning)) return;
        try
        {
            var summary = await _shifts.CloseAsync(CurrentShift.Id, counted, CloseNotes, _userId);
            Summary = summary; CurrentShift = null; CountedCash = ""; CloseNotes = "";
            Status = $"Shift closed. Expected {summary.ExpectedCash:N2}, counted {summary.CountedCash:N2}, variance {summary.CashVariance:N2}.";
            await LoadListAsync();
        }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Closing the shift"); }
    }

    private async Task ViewSelectedAsync()
    {
        if (SelectedShift is null) return;
        try { Summary = await _shifts.GetSummaryAsync(SelectedShift.Id, _userId); Status = $"Showing totals for the shift opened {SelectedShift.OpenedAtUtc.ToLocalTime():g}."; }
        catch (Exception ex) { Status = UserErrorHandler.GetMessage(ex, "Loading the shift summary"); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
