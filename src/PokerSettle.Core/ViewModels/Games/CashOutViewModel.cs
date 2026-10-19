using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Players;
using PokerSettle.Core.Services;
using PokerSettle.Core.Settlement;

namespace PokerSettle.Core.ViewModels.Games;

/// <summary>One player's final amount on the cash-out screen.</summary>
public sealed partial class CashOutRow : ObservableObject
{
    private readonly CashOutViewModel _owner;
    private bool _suppress;

    internal CashOutRow(CashOutViewModel owner, GameEntry entry, long buyInTotal, int buyInCount, string buyInText)
    {
        _owner = owner;
        PlayerId = entry.PlayerId;
        Name = entry.Player.Name;
        Initials = PlayerNames.Initials(entry.Player.Name);
        Color = entry.Player.Color;
        BuyInTotal = buyInTotal;
        BuyInCount = buyInCount;
        BuyInText = buyInText;
        SavedAmount = entry.Entry.CashOut;
        Amount = entry.Entry.CashOut;
    }

    public long PlayerId { get; }
    public string Name { get; }
    public string Initials { get; }
    public string Color { get; }
    public long BuyInTotal { get; }
    public int BuyInCount { get; }
    public string BuyInText { get; }
    public string AmountDescription => $"Final amount for {Name}";

    /// <summary>Parsed amount (game units); null when empty or invalid.</summary>
    public long? Amount { get; internal set; }

    internal long? SavedAmount { get; set; }

    internal bool IsInvalid { get; set; }

    [ObservableProperty]
    public partial string? AmountText { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial string ResultText { get; set; } = "—";

    [ObservableProperty]
    public partial int ResultSign { get; set; }

    [ObservableProperty]
    public partial string? MoneyHint { get; set; }

    internal void SetTextSilently(string? text)
    {
        _suppress = true;
        AmountText = text;
        _suppress = false;
    }

    partial void OnAmountTextChanged(string? value)
    {
        if (!_suppress) _owner.OnRowEdited(this);
    }
}

/// <summary>
/// Cash-out: enter each player's final amount (0 is fine) and watch results update; the footer shows
/// what's left to allocate. An unbalanced game is never finalized silently: fix it, split the
/// difference equally, or assign it to one player.
/// </summary>
public sealed partial class CashOutViewModel : ViewModelBase, IQueryReceiver
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(350);

    private readonly Dictionary<long, CancellationTokenSource> _pendingSaves = [];
    private Task _saveChain = Task.CompletedTask;
    private long _gameId;
    private GameSnapshot? _game;
    private GameMoney? _money;

    public CashOutViewModel(CoreServices services) : base(services)
    {
    }

    public ObservableCollection<CashOutRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "Cash out";

    [ObservableProperty]
    public partial string AmountLabel { get; set; } = "Final amount";

    [ObservableProperty]
    public partial string TotalBuyInText { get; set; } = "";

    [ObservableProperty]
    public partial string TotalCashOutText { get; set; } = "";

    [ObservableProperty]
    public partial string LeftText { get; set; } = "";

    [ObservableProperty]
    public partial int LeftSign { get; set; }

    [ObservableProperty]
    public partial bool IsBalanced { get; set; }

    [ObservableProperty]
    public partial bool CanFillRemaining { get; set; }

    [ObservableProperty]
    public partial string FillRemainingText { get; set; } = "";

    [ObservableProperty]
    public partial string FillRemainingDescription { get; set; } = "";

    [ObservableProperty]
    public partial bool ShowDiscrepancy { get; set; }

    [ObservableProperty]
    public partial string DiscrepancyText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinueUnbalanced))]
    public partial bool IsFixSelected { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinueUnbalanced))]
    public partial bool IsSplitSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinueUnbalanced))]
    public partial bool IsAssignSelected { get; set; }

    [ObservableProperty]
    public partial string SplitText { get; set; } = "";

    [ObservableProperty]
    public partial string AssignText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> PlayerNames { get; set; } = [];

    [ObservableProperty]
    public partial int AssignIndex { get; set; }

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    public partial string? WarningText { get; set; }

    public bool CanContinueUnbalanced => !IsFixSelected;

    // The three options are exclusive here too, not only in the radio group on screen.
    partial void OnIsFixSelectedChanged(bool value)
    {
        if (!value) return;
        IsSplitSelected = false;
        IsAssignSelected = false;
        ErrorText = null;
    }

    partial void OnIsSplitSelectedChanged(bool value)
    {
        if (!value) return;
        IsFixSelected = false;
        IsAssignSelected = false;
        ErrorText = null;
    }

    partial void OnIsAssignSelectedChanged(bool value)
    {
        if (!value) return;
        IsFixSelected = false;
        IsSplitSelected = false;
        ErrorText = null;
    }

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (QueryLong(query, Routes.GameId) is { } id) _gameId = id;
    }

    protected override async Task OnAppearingCoreAsync()
    {
        var game = await Data.GetGameAsync(_gameId);
        if (game is null || game.Game.IsFinalized)
        {
            await Navigation.GoBackAsync();
            return;
        }
        _game = game;
        _money = new GameMoney(Money, game.Game.Settings);
        Title = $"Cash out · {game.Game.Title}";
        AmountLabel = game.Game.Settings.ChipMode ? "Final chips" : "Final amount";

        Rows.Clear();
        foreach (var entry in game.Entries)
        {
            var total = game.BuyInTotal(entry.PlayerId);
            var count = game.BuyInCount(entry.PlayerId);
            var buyInText = count == 1 ? $"1 buy-in · {_money.Units(total)}" : $"{count} buy-ins · {_money.Units(total)}";
            var row = new CashOutRow(this, entry, total, count, buyInText);
            row.SetTextSilently(entry.Entry.CashOut is { } c ? _money.ForInput(c) : null);
            UpdateRowResult(row);
            Rows.Add(row);
        }
        PlayerNames = Rows.Select(r => r.Name).ToList();
        var noBuyIns = Rows.Where(r => r.BuyInCount == 0).Select(r => r.Name).ToList();
        WarningText = noBuyIns.Count == 0 ? null : $"No buy-ins recorded for {string.Join(", ", noBuyIns)}.";
        UpdateTotals();
    }

    protected override void OnDisappearingCore() => _ = FlushSavesAsync();

    internal void OnRowEdited(CashOutRow row)
    {
        row.Error = null;
        ErrorText = null;
        UpdateRowResult(row);
        UpdateTotals();
        ScheduleSave(row);
    }

    private void UpdateRowResult(CashOutRow row)
    {
        var money = _money!;
        if (string.IsNullOrWhiteSpace(row.AmountText))
        {
            row.Amount = null;
            row.IsInvalid = false;
            row.ResultText = "—";
            row.ResultSign = 0;
            row.MoneyHint = null;
            return;
        }
        var parsed = money.Parse(row.AmountText, allowZero: true);
        if (!parsed.Success)
        {
            row.Amount = null;
            row.IsInvalid = true;
            row.Error = parsed.Error;
            row.ResultText = "—";
            row.ResultSign = 0;
            row.MoneyHint = null;
            return;
        }
        row.IsInvalid = false;
        row.Amount = parsed.Value;
        var net = parsed.Value - row.BuyInTotal;
        row.ResultText = money.SignedUnits(net);
        row.ResultSign = Math.Sign(net);
        row.MoneyHint = money.MoneyHint(parsed.Value);
    }

    private void UpdateTotals()
    {
        var money = _money!;
        var buyIns = Rows.Sum(r => r.BuyInTotal);
        var cashOuts = Rows.Sum(r => r.Amount ?? 0);
        TotalBuyInText = money.UnitsWithMoney(buyIns);
        TotalCashOutText = money.UnitsWithMoney(cashOuts);
        var left = buyIns - cashOuts;
        var empty = Rows.Where(r => r.Amount is null && !r.IsInvalid).ToList();
        var allEntered = empty.Count == 0 && Rows.All(r => !r.IsInvalid);

        IsBalanced = left == 0 && allEntered;
        if (IsBalanced)
        {
            LeftText = "Balanced ✓";
            LeftSign = 1;
        }
        else if (left >= 0)
        {
            LeftText = $"Left to allocate {money.Units(left)}";
            LeftSign = 0;
        }
        else
        {
            LeftText = $"Over by {money.Units(-left)}";
            LeftSign = -1;
        }

        CanFillRemaining = empty.Count == 1 && left >= 0 && Rows.All(r => !r.IsInvalid);
        FillRemainingText = CanFillRemaining ? $"Fill remaining ({money.Units(left)})" : "";
        FillRemainingDescription = CanFillRemaining ? $"Give {empty[0].Name} the remaining {money.Units(left)}" : "";

        if (IsBalanced) ShowDiscrepancy = false;
        UpdateDiscrepancyTexts();
    }

    private void UpdateDiscrepancyTexts()
    {
        if (_money is null || Rows.Count == 0) return;
        var difference = Rows.Sum(r => r.Amount ?? 0) - Rows.Sum(r => r.BuyInTotal);
        if (difference == 0) return;
        var abs = _money.Units(Math.Abs(difference));
        DiscrepancyText = difference > 0
            ? $"Cash-outs are {abs} more than buy-ins."
            : $"Cash-outs are {abs} less than buy-ins.";
        var shares = Apportionment.SplitEvenly(-difference, Rows.Count);
        var min = shares.Min();
        var max = shares.Max();
        var each = min == max ? _money.SignedUnits(min) : $"{_money.SignedUnits(min)} to {_money.SignedUnits(max)}";
        SplitText = $"Split equally: {each} on everyone's result";
        AssignText = $"Assign {_money.SignedUnits(-difference)} to one player";
    }

    [RelayCommand]
    private void FillRemaining()
    {
        var empty = Rows.Where(r => r.Amount is null && !r.IsInvalid).ToList();
        if (empty.Count != 1 || _money is null) return;
        var left = Rows.Sum(r => r.BuyInTotal) - Rows.Sum(r => r.Amount ?? 0);
        if (left < 0) return;
        empty[0].AmountText = _money.ForInput(left);
    }

    [RelayCommand]
    private Task ContinueAsync() => RunSafeAsync(async () =>
    {
        if (_game is null) return;
        await FlushSavesAsync();
        ErrorText = null;

        var missing = false;
        foreach (var row in Rows)
        {
            if (row.IsInvalid)
            {
                missing = true;
            }
            else if (row.Amount is null)
            {
                row.Error = "Enter an amount (0 is fine)";
                missing = true;
            }
        }
        if (Rows.Count < 2)
        {
            ErrorText = "A game needs at least 2 players.";
            Log.ValidationFailed("cash-out", "fewer than 2 players");
            return;
        }
        if (missing)
        {
            ErrorText = "Enter every player's final amount.";
            Log.ValidationFailed("cash-out", "missing or invalid amounts");
            return;
        }

        var difference = Rows.Sum(r => r.Amount!.Value) - Rows.Sum(r => r.BuyInTotal);
        var query = new Dictionary<string, object> { [Routes.GameId] = _gameId };
        if (difference != 0)
        {
            if (!ShowDiscrepancy)
            {
                ShowDiscrepancy = true;
                IsFixSelected = true;
                Log.ValidationFailed("cash-out", $"unbalanced by {difference}");
                return;
            }
            if (IsFixSelected)
            {
                ErrorText = "Fix the amounts until the footer shows Balanced ✓, or pick another option.";
                return;
            }
            if (IsAssignSelected)
            {
                if (AssignIndex < 0 || AssignIndex >= Rows.Count)
                {
                    ErrorText = "Choose who takes the difference.";
                    return;
                }
                query[Routes.Resolution] = (long)DiscrepancyResolution.AssignToPlayer;
                query[Routes.AssignedPlayerId] = Rows[AssignIndex].PlayerId;
            }
            else
            {
                query[Routes.Resolution] = (long)DiscrepancyResolution.SplitEqually;
            }
        }
        await Navigation.GoToAsync(Routes.Review, query);
    });

    private void ScheduleSave(CashOutRow row)
    {
        if (_pendingSaves.Remove(row.PlayerId, out var previous)) previous.Cancel();
        var cts = new CancellationTokenSource();
        _pendingSaves[row.PlayerId] = cts;
        _ = SaveLaterAsync(row, cts);
    }

    private async Task SaveLaterAsync(CashOutRow row, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(SaveDelay, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        if (_pendingSaves.TryGetValue(row.PlayerId, out var current) && current == cts) _pendingSaves.Remove(row.PlayerId);
        await EnqueueSave(row);
    }

    /// <summary>Saves run one after another, so an older value can never overwrite a newer one.</summary>
    private Task EnqueueSave(CashOutRow row)
    {
        if (row.IsInvalid || row.Amount == row.SavedAmount) return _saveChain;
        _saveChain = SaveAfterAsync(_saveChain, row, row.Amount);
        return _saveChain;
    }

    private async Task SaveAfterAsync(Task previous, CashOutRow row, long? amount)
    {
        await previous;
        try
        {
            await Data.SetCashOutAsync(_gameId, row.PlayerId, amount);
            row.SavedAmount = amount;
        }
        catch (GameNotEditableException)
        {
            // Finalized elsewhere; nothing to save.
        }
        catch (Exception ex)
        {
            Log.CommandFailed(ex, "CashOut.Save");
            Snackbar.ShowError();
        }
    }

    /// <summary>Saves every edited amount now (before review, and when leaving the page).</summary>
    public async Task FlushSavesAsync()
    {
        foreach (var cts in _pendingSaves.Values) cts.Cancel();
        _pendingSaves.Clear();
        foreach (var row in Rows) _ = EnqueueSave(row);
        await _saveChain;
        Messenger.Send(new DataChangedMessage(DataArea.Games, _gameId, this));
    }
}
