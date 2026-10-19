using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Services;
using PokerSettle.Core.ViewModels.Players;

namespace PokerSettle.Core.ViewModels.Games;

/// <summary>Late players: the same picker and suggestions as a new game.</summary>
public sealed partial class AddPlayersViewModel : ViewModelBase, IQueryReceiver
{
    private long _gameId;
    private bool _loaded;

    public AddPlayersViewModel(CoreServices services) : base(services)
    {
        Picker = new PlayerPickerViewModel(Data, Messenger, Snackbar, Log);
        Picker.SelectionChanged += (_, _) => AddText = Picker.Selected.Count switch
        {
            0 => "Add players",
            1 => $"Add {Picker.Selected[0].Name}",
            _ => $"Add {Picker.Selected.Count} players",
        };
    }

    public PlayerPickerViewModel Picker { get; }

    [ObservableProperty]
    public partial string AddText { get; set; } = "Add players";

    [ObservableProperty]
    public partial string Hint { get; set; } = "";

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (QueryLong(query, Routes.GameId) is { } id) _gameId = id;
    }

    protected override async Task OnAppearingCoreAsync()
    {
        if (_loaded) return;
        _loaded = true;
        var game = await Data.GetGameAsync(_gameId);
        if (game is null)
        {
            await Navigation.GoBackAsync();
            return;
        }
        var money = new Money.GameMoney(Money, game.Game.Settings);
        Hint = game.Game.Settings.StartWithBuyIn
            ? $"Each new player starts with one buy-in of {money.Units(game.Game.Settings.DefaultBuyIn)}."
            : "New players start with no buy-ins.";
        await Picker.LoadAsync(_gameId, locked: game.Entries.Select(e => e.PlayerId));
    }

    [RelayCommand]
    private Task AddAsync() => RunSafeAsync(async () =>
    {
        var chosen = Picker.Selected.ToList();
        if (chosen.Count == 0)
        {
            await Navigation.GoBackAsync();
            return;
        }
        var added = new List<long>();
        foreach (var chip in chosen)
        {
            await Data.AddPlayerToGameAsync(_gameId, chip.PlayerId);
            added.Add(chip.PlayerId);
        }
        Messenger.Send(new DataChangedMessage(DataArea.Games, _gameId));
        await Navigation.GoBackAsync();

        var names = chosen.Count == 1 ? chosen[0].Name : $"{chosen.Count} players";
        var gameId = _gameId;
        Snackbar.ShowUndo($"Added {names}", async () =>
        {
            foreach (var id in added) await Data.RemovePlayerFromGameAsync(gameId, id);
            Messenger.Send(new DataChangedMessage(DataArea.Games, gameId));
        });
    });
}
