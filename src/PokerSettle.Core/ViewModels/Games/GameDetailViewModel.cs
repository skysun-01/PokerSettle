using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Services;
using PokerSettle.Core.Settlement;

namespace PokerSettle.Core.ViewModels.Games;

/// <summary>
/// A game's details: results, buy-ins, adjustments and payments. Finalized games are read-only except
/// for paid marks; Share/Copy produce the text summary; Reopen, New game with same players, Delete (Undo).
/// </summary>
public sealed partial class GameDetailViewModel : ViewModelBase, IQueryReceiver
{
    private readonly IClipboardService _clipboard;
    private readonly IShareService _share;
    private readonly ILauncherService _launcher;
    private readonly IAppEnvironment _environment;
    private long _gameId;
    private GameSnapshot? _game;

    public GameDetailViewModel(CoreServices services, IClipboardService clipboard, IShareService share, ILauncherService launcher, IAppEnvironment environment)
        : base(services)
    {
        _clipboard = clipboard;
        _share = share;
        _launcher = launcher;
        _environment = environment;
        Messenger.Register<GameDetailViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (ReferenceEquals(m.Source, r) || !m.AffectsGame(r._gameId)) return;
            if (r.IsActive) _ = r.RunSafeAsync(r.LoadAsync, "Refresh");
            else r.IsStale = true;
        });
    }

    public ObservableCollection<ResultRow> Results { get; } = [];
    public ObservableCollection<TransferItem> Transfers { get; } = [];
    public ObservableCollection<string> BuyInLines { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "Game";

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "";

    [ObservableProperty]
    public partial string? Notes { get; set; }

    [ObservableProperty]
    public partial bool IsFinalized { get; set; }

    [ObservableProperty]
    public partial bool IsInProgress { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string PotText { get; set; } = "";

    [ObservableProperty]
    public partial string SettlementText { get; set; } = "";

    [ObservableProperty]
    public partial string? AdjustmentText { get; set; }

    [ObservableProperty]
    public partial string PaymentsTitle { get; set; } = "Payments";

    [ObservableProperty]
    public partial string? NoPaymentsText { get; set; }

    [ObservableProperty]
    public partial bool HasNotes { get; set; }

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (QueryLong(query, Routes.GameId) is { } id) _gameId = id;
    }

    protected override Task OnAppearingCoreAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var game = await Data.GetGameAsync(_gameId);
        IsStale = false;
        if (game is null)
        {
            await Navigation.GoBackAsync();
            return;
        }
        _game = game;
        var money = new GameMoney(Money, game.Game.Settings);
        var settings = game.Game.Settings;
        Title = game.Game.Title;
        Subtitle = string.IsNullOrWhiteSpace(game.Game.Location)
            ? Money.FormatDate(game.Game.Date)
            : $"{Money.FormatDate(game.Game.Date)} · {game.Game.Location}";
        Notes = game.Game.Notes;
        HasNotes = !string.IsNullOrWhiteSpace(Notes);
        IsFinalized = game.Game.IsFinalized;
        IsInProgress = !IsFinalized;
        StatusText = IsFinalized && game.Game.FinalizedAt is { } at ? $"Finalized {Money.FormatDateTime(at)}" : "In progress";
        PotText = $"Pot {money.UnitsWithMoney(game.Pot)} · {game.Entries.Count} players · {game.BuyIns.Count} buy-ins";
        var rounding = settings.Rounding switch
        {
            RoundingMode.Nearest1 => $", rounded to {Money.Format(100)}",
            RoundingMode.Nearest10 => $", rounded to {Money.Format(1_000)}",
            _ => "",
        };
        var banker = settings.BankerId is { } b ? game.FindPlayer(b)?.Name : null;
        SettlementText = (settings.SettlementMode == SettlementMode.Banker ? $"Banker: {banker ?? "?"}" : "Fewest payments") + rounding
            + (settings.ChipMode && settings.ChipRate is { } rate ? $" · {Money.FormatChips(rate.Chips)} = {Money.Format(rate.Money)}" : "");

        Results.Clear();
        if (IsFinalized)
        {
            foreach (var row in GameRowFactory.StoredResults(game, money)) Results.Add(row);
        }

        BuyInLines.Clear();
        foreach (var entry in game.Entries)
        {
            var buyIns = game.BuyIns.Where(x => x.PlayerId == entry.PlayerId).ToList();
            var times = string.Join(", ", buyIns.Select(x => $"{money.Units(x.Amount)} at {x.At.ToLocalTime().ToString("HH:mm", Money.Culture)}"));
            var cash = entry.Entry.CashOut is { } c ? $" → out {money.Units(c)}" : "";
            BuyInLines.Add($"{entry.Player.Name}: {(buyIns.Count == 0 ? "no buy-ins" : times)}{cash}");
        }

        AdjustmentText = game.Adjustments.Count == 0 ? null : GameRowFactory.AdjustmentSummary(game,
            game.Adjustments.Where(a => a.PlayerId is not null).Select(a => new AdjustmentShare(a.PlayerId!.Value, a.Amount)).ToList(),
            game.Adjustments[0].Kind, money);

        Transfers.Clear();
        if (IsFinalized)
        {
            foreach (var t in game.Transfers.OrderBy(t => t.Id))
            {
                var to = game.FindPlayer(t.ToId);
                Transfers.Add(new TransferItem
                {
                    Id = t.Id,
                    FromName = game.FindPlayer(t.FromId)?.Name ?? "?",
                    ToName = to?.Name ?? "?",
                    AmountText = Money.Format(t.Amount),
                    Amount = t.Amount,
                    UpiId = to?.UpiId,
                    IsPaid = t.IsPaid,
                    PaidText = t.IsPaid && t.PaidAt is { } paidAt ? $"Paid {Money.FormatShortDate(DateOnly.FromDateTime(paidAt.LocalDateTime))}" : null,
                    // UPI is rupees-only.
                    CanPayViaUpi = _environment.IsAndroid && Money.CurrencySymbol == "₹" && !string.IsNullOrWhiteSpace(to?.UpiId),
                    TogglePaidCommand = TogglePaidCommand,
                    CopyUpiCommand = CopyUpiCommand,
                    PayUpiCommand = PayUpiCommand,
                });
            }
        }
        UpdatePaymentsTitle();
        NoPaymentsText = IsFinalized && Transfers.Count == 0 ? "Everyone was even. No payments needed." : null;
    }

    private void UpdatePaymentsTitle()
    {
        var paid = Transfers.Count(t => t.IsPaid);
        PaymentsTitle = Transfers.Count == 0 ? "Payments" : $"Payments · {paid} of {Transfers.Count} paid";
    }

    [RelayCommand]
    private Task TogglePaidAsync(TransferItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;
        var paid = !item.IsPaid;
        await SetPaidAsync(item, paid);
        Snackbar.ShowUndo(paid ? $"{item.Text}: marked paid" : $"{item.Text}: marked unpaid", () => SetPaidAsync(item, !paid));
    });

    private async Task SetPaidAsync(TransferItem item, bool paid)
    {
        var updated = await Data.SetTransferPaidAsync(item.Id, paid);
        item.IsPaid = paid;
        item.PaidText = paid && updated?.PaidAt is { } at ? $"Paid {Money.FormatShortDate(DateOnly.FromDateTime(at.LocalDateTime))}" : null;
        UpdatePaymentsTitle();
        Messenger.Send(new DataChangedMessage(DataArea.Payments, _gameId, this));
    }

    [RelayCommand]
    private Task CopyUpiAsync(TransferItem? item) => RunSafeAsync(async () =>
    {
        if (item?.UpiId is not { } upi) return;
        await _clipboard.SetTextAsync(upi);
        Snackbar.Show($"Copied {item.ToName}'s UPI ID");
    });

    /// <summary>Phase 5: opens a UPI app with the payee and amount filled in. Some apps refuse these links, so Copy stays available.</summary>
    [RelayCommand]
    private Task PayUpiAsync(TransferItem? item) => RunSafeAsync(async () =>
    {
        if (item?.UpiId is not { } upi || _game is null) return;
        var uri = UpiLink.Build(upi, item.ToName, item.Amount, $"Poker {_game.Game.Title}");
        if (!await _launcher.TryOpenAsync(uri))
        {
            await _clipboard.SetTextAsync(upi);
            Snackbar.Show("No UPI app opened the link, so the UPI ID was copied instead.");
        }
    });

    [RelayCommand]
    private Task ShareAsync() => RunSafeAsync(async () =>
    {
        // Fresh read, so paid marks made on this page are included.
        if (await Data.GetGameAsync(_gameId) is not { } game) return;
        await _share.ShareTextAsync(game.Game.Title, ShareText.Build(game, Money));
    });

    [RelayCommand]
    private Task CopyAsync() => RunSafeAsync(async () =>
    {
        if (await Data.GetGameAsync(_gameId) is not { } game) return;
        await _clipboard.SetTextAsync(ShareText.Build(game, Money));
        Snackbar.Show("Summary copied");
    });

    [RelayCommand]
    private Task ResumeAsync() => RunSafeAsync(() =>
        Navigation.GoToAsync($"../{Routes.LiveGame}", new Dictionary<string, object> { [Routes.GameId] = _gameId }));

    [RelayCommand]
    private Task ReopenAsync() => RunSafeAsync(async () =>
    {
        if (_game is null || !_game.Game.IsFinalized) return;
        var anyPaid = _game.Transfers.Any(t => t.IsPaid);
        var message = "You can change buy-ins and cash-outs, then finalize again. Results leave the board until then."
            + (anyPaid ? " Payments are recalculated, so paid marks may reset." : "");
        if (!await Dialogs.ConfirmAsync("Reopen this game?", message, "Reopen", "Cancel")) return;
        try
        {
            await Data.ReopenGameAsync(_gameId);
        }
        catch (GameInProgressException)
        {
            await Dialogs.AlertAsync("Another game is in progress", "Finish or delete the game in progress first, then reopen this one.");
            return;
        }
        Messenger.Send(new DataChangedMessage(DataArea.Games | DataArea.Results | DataArea.Payments, _gameId, this));
        await Navigation.GoToAsync($"../{Routes.LiveGame}", new Dictionary<string, object> { [Routes.GameId] = _gameId });
    });

    [RelayCommand]
    private Task NewGameSameSettingsAsync() => RunSafeAsync(async () =>
    {
        if (await Data.GetInProgressGameIdAsync() is { } running)
        {
            Snackbar.Show("A game is already in progress. Finish it first.");
            await Navigation.GoToAsync(Routes.LiveGame, new Dictionary<string, object> { [Routes.GameId] = running });
            return;
        }
        await Navigation.GoToAsync(Routes.NewGame, new Dictionary<string, object> { [Routes.FromGameId] = _gameId });
    });

    [RelayCommand]
    private Task DeleteAsync() => RunSafeAsync(async () =>
    {
        var record = await Data.DeleteGameAsync(_gameId);
        if (record is null) return;
        Messenger.Send(new DataChangedMessage(DataArea.All, _gameId, this));
        await Navigation.GoBackAsync();
        Snackbar.ShowUndo($"Deleted {record.Game.Title}", async () =>
        {
            try
            {
                await Data.RestoreGameAsync(record);
            }
            catch (GameInProgressException)
            {
                Snackbar.Show("Can't restore it while another game is in progress.");
                return;
            }
            Messenger.Send(new DataChangedMessage(DataArea.All, record.Game.Id));
        });
    });
}

/// <summary>Builds UPI deep links (upi://pay).</summary>
public static class UpiLink
{
    public static string Build(string upiId, string payeeName, long amountPaise, string note) =>
        "upi://pay?pa=" + Uri.EscapeDataString(upiId.Trim())
        + "&pn=" + Uri.EscapeDataString(payeeName)
        + "&am=" + (amountPaise / 100m).ToString("0.00", CultureInfo.InvariantCulture)
        + "&cu=INR"
        + "&tn=" + Uri.EscapeDataString(note.Length > 50 ? note[..50] : note);
}
