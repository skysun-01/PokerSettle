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

namespace PokerSettle.Core.ViewModels.Games;

public sealed class BuyInRow
{
    public required long Id { get; init; }
    public required string AmountText { get; init; }
    public required string TimeText { get; init; }
    public required IRelayCommand<BuyInRow> RemoveCommand { get; init; }

    public string RemoveDescription => $"Remove buy-in of {AmountText} at {TimeText}";
}

/// <summary>One player's card in the live game.</summary>
public sealed partial class LiveEntryItem : ObservableObject
{
    private readonly LiveGameViewModel _owner;

    internal LiveEntryItem(LiveGameViewModel owner, long playerId, string name, string color)
    {
        _owner = owner;
        PlayerId = playerId;
        Name = name;
        Color = color;
        Initials = PlayerNames.Initials(name);
    }

    public long PlayerId { get; }
    public string Name { get; }
    public string Initials { get; }
    public string Color { get; }
    public ObservableCollection<BuyInRow> BuyIns { get; } = [];

    internal long BuyInTotal { get; set; }
    internal long? CashOut { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    [ObservableProperty]
    public partial string BuyInButtonText { get; set; } = "";

    [ObservableProperty]
    public partial string BuyInButtonDescription { get; set; } = "";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsCustomOpen { get; set; }

    [ObservableProperty]
    public partial string? CustomText { get; set; }

    [ObservableProperty]
    public partial string? CustomError { get; set; }

    [ObservableProperty]
    public partial bool IsCashOutOpen { get; set; }

    [ObservableProperty]
    public partial string? CashOutText { get; set; }

    [ObservableProperty]
    public partial string? CashOutError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    public partial bool IsCashedOut { get; set; }

    [ObservableProperty]
    public partial string? CashedOutText { get; set; }

    [ObservableProperty]
    public partial string? ResultText { get; set; }

    [ObservableProperty]
    public partial int ResultSign { get; set; }

    [ObservableProperty]
    public partial bool HasBuyIns { get; set; }

    public bool IsPlaying => !IsCashedOut;

    [RelayCommand]
    private Task AddBuyIn() => _owner.AddBuyInAsync(this);

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded && HasBuyIns;

    [RelayCommand]
    private void OpenCustom()
    {
        IsCashOutOpen = false;
        CustomError = null;
        CustomText = _owner.DefaultBuyInInput;
        IsCustomOpen = true;
    }

    [RelayCommand]
    private void CancelCustom() => IsCustomOpen = false;

    [RelayCommand]
    private Task ConfirmCustom() => _owner.AddCustomBuyInAsync(this);

    [RelayCommand]
    private void OpenCashOut()
    {
        IsCustomOpen = false;
        CashOutError = null;
        CashOutText = null;
        IsCashOutOpen = true;
    }

    [RelayCommand]
    private void CancelCashOut() => IsCashOutOpen = false;

    [RelayCommand]
    private Task ConfirmCashOut() => _owner.CashOutAsync(this);

    [RelayCommand]
    private Task ReopenCashOut() => _owner.ReopenCashOutAsync(this);

    [RelayCommand]
    private Task Remove() => _owner.RemovePlayerAsync(this);
}

/// <summary>
/// The live game: a card per player with a big "+ Buy-in", custom amounts, removable buy-ins, early
/// cash-out, late players, and Undo for every action. Everything is saved the moment it happens.
/// </summary>
public sealed partial class LiveGameViewModel : ViewModelBase, IQueryReceiver
{
    private readonly IHapticsService _haptics;
    private long _gameId;
    private GameSnapshot? _game;
    private GameMoney? _money;

    public LiveGameViewModel(CoreServices services, IHapticsService haptics) : base(services)
    {
        _haptics = haptics;
        // Changes made elsewhere while this page is open (e.g. Undo of "add players").
        Messenger.Register<LiveGameViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (ReferenceEquals(m.Source, r) || !r.IsActive || !m.AffectsGame(r._gameId)) return;
            _ = r.RunSafeAsync(r.ReloadAsync, "Refresh");
        });
    }

    public ObservableCollection<LiveEntryItem> Players { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "Game";

    [ObservableProperty]
    public partial string Subtitle { get; set; } = "";

    [ObservableProperty]
    public partial string PotText { get; set; } = "";

    [ObservableProperty]
    public partial string? PotMoneyText { get; set; }

    [ObservableProperty]
    public partial string StatsText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasPlayers { get; set; }

    [ObservableProperty]
    public partial string CustomLabel { get; set; } = "Amount";

    internal string DefaultBuyInInput => _money?.ForInput(_game?.Game.Settings.DefaultBuyIn ?? 0) ?? "";

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (QueryLong(query, Routes.GameId) is { } id) _gameId = id;
    }

    protected override Task OnAppearingCoreAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        var game = await Data.GetGameAsync(_gameId);
        if (game is null)
        {
            await Navigation.GoBackAsync();
            return;
        }
        if (game.Game.IsFinalized)
        {
            await Navigation.GoToAsync($"../{Routes.GameDetail}", new Dictionary<string, object> { [Routes.GameId] = _gameId });
            return;
        }
        _game = game;
        _money = new GameMoney(Money, game.Game.Settings);
        Sync(game);
    }

    private void Sync(GameSnapshot game)
    {
        var money = _money!;
        var settings = game.Game.Settings;
        Title = game.Game.Title;
        Subtitle = string.IsNullOrWhiteSpace(game.Game.Location)
            ? Money.FormatDate(game.Game.Date)
            : $"{Money.FormatDate(game.Game.Date)} · {game.Game.Location}";
        PotText = money.Units(game.Pot);
        PotMoneyText = money.MoneyHint(game.Pot);
        var cashedOut = game.Entries.Count(e => e.Entry.CashOut is not null);
        StatsText = $"{game.Entries.Count} players · {game.BuyIns.Count} buy-ins" + (cashedOut > 0 ? $" · {cashedOut} cashed out" : "");
        CustomLabel = settings.ChipMode ? "Chips" : "Amount";

        var existing = Players.ToDictionary(p => p.PlayerId);
        var ordered = new List<LiveEntryItem>();
        foreach (var entry in game.Entries)
        {
            if (!existing.TryGetValue(entry.PlayerId, out var item))
                item = new LiveEntryItem(this, entry.PlayerId, entry.Player.Name, entry.Player.Color);
            Update(item, game, entry);
            ordered.Add(item);
        }

        // Keep the same item objects so the list doesn't jump; only add/remove/move what changed.
        for (var i = Players.Count - 1; i >= 0; i--)
        {
            if (!ordered.Contains(Players[i])) Players.RemoveAt(i);
        }
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = Players.IndexOf(ordered[i]);
            if (current < 0) Players.Insert(i, ordered[i]);
            else if (current != i) Players.Move(current, i);
        }
        HasPlayers = Players.Count > 0;
    }

    private void Update(LiveEntryItem item, GameSnapshot game, GameEntry entry)
    {
        var money = _money!;
        var buyIns = game.BuyIns.Where(b => b.PlayerId == entry.PlayerId).ToList();
        item.BuyInTotal = buyIns.Sum(b => b.Amount);
        item.CashOut = entry.Entry.CashOut;
        item.HasBuyIns = buyIns.Count > 0;
        item.SummaryText = buyIns.Count switch
        {
            0 => "No buy-ins yet",
            1 => $"1 buy-in · {money.UnitsWithMoney(item.BuyInTotal)}",
            _ => $"{buyIns.Count} buy-ins · {money.UnitsWithMoney(item.BuyInTotal)}",
        };
        var defaultBuyIn = game.Game.Settings.DefaultBuyIn;
        item.BuyInButtonText = money.BuyInButton(defaultBuyIn);
        item.BuyInButtonDescription = $"Add a buy-in of {money.Units(defaultBuyIn)} for {item.Name}";

        item.BuyIns.Clear();
        foreach (var b in buyIns)
        {
            item.BuyIns.Add(new BuyInRow
            {
                Id = b.Id,
                AmountText = money.Units(b.Amount),
                TimeText = b.At.ToLocalTime().ToString("HH:mm", Money.Culture),
                RemoveCommand = RemoveBuyInCommand,
            });
        }
        if (!item.HasBuyIns) item.IsExpanded = false;

        item.IsCashedOut = entry.Entry.CashOut is not null;
        if (entry.Entry.CashOut is { } cash)
        {
            item.CashedOutText = $"Cashed out {money.UnitsWithMoney(cash)}";
            var net = cash - item.BuyInTotal;
            item.ResultText = money.SignedUnitsWithMoney(net);
            item.ResultSign = Math.Sign(net);
            item.IsCashOutOpen = false;
            item.IsCustomOpen = false;
        }
        else
        {
            item.CashedOutText = null;
            item.ResultText = null;
            item.ResultSign = 0;
        }
    }

    internal Task AddBuyInAsync(LiveEntryItem item) => RunSafeAsync(async () =>
    {
        if (_game is null) return;
        await AddBuyInCoreAsync(item, _game.Game.Settings.DefaultBuyIn);
    }, "AddBuyIn");

    internal Task AddCustomBuyInAsync(LiveEntryItem item) => RunSafeAsync(async () =>
    {
        if (_money is null) return;
        var parsed = _money.Parse(item.CustomText, allowZero: false);
        if (!parsed.Success)
        {
            item.CustomError = parsed.Error;
            return;
        }
        item.IsCustomOpen = false;
        await AddBuyInCoreAsync(item, parsed.Value);
    }, "AddCustomBuyIn");

    private async Task AddBuyInCoreAsync(LiveEntryItem item, long amount)
    {
        BuyIn buyIn;
        try
        {
            buyIn = await Data.AddBuyInAsync(_gameId, item.PlayerId, amount);
        }
        catch (GameNotEditableException)
        {
            await ReloadAsync();
            return;
        }
        _haptics.Click();
        await ReloadAsync();
        Messenger.Send(new DataChangedMessage(DataArea.Games, _gameId, this));
        var text = $"{_money!.Units(amount)} buy-in for {item.Name}";
        Snackbar.ShowUndo(text, () => UndoAsync(async () =>
        {
            await Data.RemoveBuyInAsync(buyIn.Id);
        }));
    }

    [RelayCommand]
    private Task RemoveBuyInAsync(BuyInRow? row) => RunSafeAsync(async () =>
    {
        if (row is null) return;
        var removed = await Data.RemoveBuyInAsync(row.Id);
        await ReloadAsync();
        if (removed is null) return;
        Messenger.Send(new DataChangedMessage(DataArea.Games, _gameId, this));
        Snackbar.ShowUndo($"Removed a {row.AmountText} buy-in", () => UndoAsync(() => Data.RestoreBuyInAsync(removed)));
    });

    internal Task CashOutAsync(LiveEntryItem item) => RunSafeAsync(async () =>
    {
        if (_money is null) return;
        var parsed = _money.Parse(item.CashOutText, allowZero: true);
        if (!parsed.Success)
        {
            item.CashOutError = parsed.Error;
            return;
        }
        await Data.SetCashOutAsync(_gameId, item.PlayerId, parsed.Value);
        await ReloadAsync();
        Messenger.Send(new DataChangedMessage(DataArea.Games, _gameId, this));
        Snackbar.ShowUndo($"{item.Name} cashed out {_money.Units(parsed.Value)}",
            () => UndoAsync(() => Data.SetCashOutAsync(_gameId, item.PlayerId, null)));
    }, "CashOut");

    internal Task ReopenCashOutAsync(LiveEntryItem item) => RunSafeAsync(async () =>
    {
        var previous = item.CashOut;
        await Data.SetCashOutAsync(_gameId, item.PlayerId, null);
        await ReloadAsync();
        Messenger.Send(new DataChangedMessage(DataArea.Games, _gameId, this));
        if (previous is { } amount)
            Snackbar.ShowUndo($"{item.Name} is back in the game", () => UndoAsync(() => Data.SetCashOutAsync(_gameId, item.PlayerId, amount)));
    }, "ReopenCashOut");

    internal Task RemovePlayerAsync(LiveEntryItem item) => RunSafeAsync(async () =>
    {
        var removed = await Data.RemovePlayerFromGameAsync(_gameId, item.PlayerId);
        await ReloadAsync();
        if (removed is null) return;
        Messenger.Send(new DataChangedMessage(DataArea.Games, _gameId, this));
        Snackbar.ShowUndo($"{item.Name} removed from the game", () => UndoAsync(() => Data.RestorePlayerInGameAsync(removed)));
    }, "RemovePlayer");

    [RelayCommand]
    private Task AddPlayersAsync() => RunSafeAsync(() =>
        Navigation.GoToAsync(Routes.AddPlayers, new Dictionary<string, object> { [Routes.GameId] = _gameId }));

    [RelayCommand]
    private Task CashOutAllAsync() => RunSafeAsync(() =>
        Navigation.GoToAsync(Routes.CashOut, new Dictionary<string, object> { [Routes.GameId] = _gameId }));

    /// <summary>Runs an Undo, then refreshes; a game finalized in the meantime can't be changed.</summary>
    private async Task UndoAsync(Func<Task> undo)
    {
        try
        {
            await undo();
            Log.UndoPerformed("live game");
        }
        catch (GameNotEditableException)
        {
            Snackbar.Show("That game is finalized now. Reopen it to make changes.");
        }
        await ReloadAsync();
        Messenger.Send(new DataChangedMessage(DataArea.Games, _gameId, this));
    }
}
