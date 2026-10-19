using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Players;
using PokerSettle.Core.Services;
using PokerSettle.Core.Stats;

namespace PokerSettle.Core.ViewModels.Players;

public sealed class PlayerGameItem
{
    public required long GameId { get; init; }
    public required string Title { get; init; }
    public required string DateText { get; init; }
    public required string NetText { get; init; }
    public required int NetSign { get; init; }
    public required string Detail { get; init; }
    public required ICommand OpenCommand { get; init; }
    public string Description => $"{Title}, {DateText}: {NetText}";
}

/// <summary>A player's page: stats, cumulative profit chart and game-by-game history.</summary>
public sealed partial class PlayerDetailViewModel : ViewModelBase, IQueryReceiver
{
    private readonly IClipboardService _clipboard;
    private long _playerId;
    private Player? _player;

    public PlayerDetailViewModel(CoreServices services, IClipboardService clipboard) : base(services)
    {
        _clipboard = clipboard;
        Messenger.Register<PlayerDetailViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (!m.Affects(DataArea.Players | DataArea.Results)) return;
            r.IsStale = true;
            if (r.IsActive) _ = r.RunSafeAsync(r.LoadAsync, "Refresh");
        });
    }

    public ObservableCollection<PlayerGameItem> Games { get; } = [];

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string? Subtitle { get; set; }
    [ObservableProperty] public partial string Initials { get; set; } = "";
    [ObservableProperty] public partial string Color { get; set; } = PlayerNames.Palette[0];
    [ObservableProperty] public partial bool IsArchived { get; set; }
    [ObservableProperty] public partial string? UpiId { get; set; }
    [ObservableProperty] public partial bool HasUpi { get; set; }
    [ObservableProperty] public partial string? Notes { get; set; }
    [ObservableProperty] public partial bool HasNotes { get; set; }

    [ObservableProperty] public partial string NetText { get; set; } = "";
    [ObservableProperty] public partial int NetSign { get; set; }
    [ObservableProperty] public partial string GamesText { get; set; } = "";
    [ObservableProperty] public partial string WinRateText { get; set; } = "";
    [ObservableProperty] public partial string AverageText { get; set; } = "";
    [ObservableProperty] public partial int AverageSign { get; set; }
    [ObservableProperty] public partial string WonText { get; set; } = "";
    [ObservableProperty] public partial string LostText { get; set; } = "";
    [ObservableProperty] public partial string BestText { get; set; } = "";
    [ObservableProperty] public partial string WorstText { get; set; } = "";
    [ObservableProperty] public partial string LastPlayedText { get; set; } = "";
    [ObservableProperty] public partial ChartData Chart { get; set; } = ChartData.Empty;
    [ObservableProperty] public partial bool HasGames { get; set; }

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (QueryLong(query, Routes.PlayerId) is { } id) _playerId = id;
    }

    protected override async Task OnAppearingCoreAsync()
    {
        if (IsStale || _player is null) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var history = await Data.GetPlayerHistoryAsync(_playerId);
        IsStale = false;
        if (history is null)
        {
            await Navigation.GoBackAsync();
            return;
        }
        var p = _player = history.Player;
        Name = p.Name;
        Subtitle = string.Join(" · ", new[] { p.Nickname, p.IsArchived ? "Archived" : null }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrEmpty(Subtitle)) Subtitle = null;
        Initials = PlayerNames.Initials(p.Name);
        Color = p.Color;
        IsArchived = p.IsArchived;
        UpiId = p.UpiId;
        HasUpi = !string.IsNullOrWhiteSpace(p.UpiId);
        Notes = p.Notes;
        HasNotes = !string.IsNullOrWhiteSpace(p.Notes);

        var stats = PlayerStatsMath.Summarize(p.Id, p.Name, p.Nickname, p.Color, p.IsArchived, history.Games);
        NetText = Money.FormatSigned(stats.Net);
        NetSign = Math.Sign(stats.Net);
        GamesText = stats.Games.ToString(Money.Culture);
        WinRateText = stats.Games == 0 ? "—" : Money.FormatPercent(stats.WinRate);
        AverageText = stats.Games == 0 ? "—" : Money.FormatSigned(stats.Average);
        AverageSign = Math.Sign(stats.Average);
        WonText = Money.Format(stats.Won);
        LostText = Money.FormatSigned(stats.Lost);
        BestText = stats.BiggestWin is { } best ? Money.FormatSigned(best) : "—";
        WorstText = stats.BiggestLoss is { } worst ? Money.FormatSigned(worst) : "—";
        LastPlayedText = stats.LastPlayed is { } last ? Money.FormatCompactDate(last) : "Never";
        Chart = ChartData.From(history.Games, Money);
        HasGames = history.Games.Count > 0;

        Games.Clear();
        foreach (var g in history.Games.Reverse())
        {
            var buyIn = g.ChipMode ? Money.FormatChips(g.BuyInTotal) : Money.Format(g.BuyInTotal);
            Games.Add(new PlayerGameItem
            {
                GameId = g.GameId,
                Title = g.Title,
                DateText = Money.FormatDate(g.Date),
                NetText = Money.FormatSigned(g.Net),
                NetSign = Math.Sign(g.Net),
                Detail = $"Bought in {buyIn}",
                OpenCommand = OpenGameCommand,
            });
        }
    }

    [RelayCommand]
    private Task EditAsync() => RunSafeAsync(() =>
        Navigation.GoToAsync(Routes.PlayerEdit, new Dictionary<string, object> { [Routes.PlayerId] = _playerId }));

    [RelayCommand]
    private Task OpenGameAsync(PlayerGameItem? item) => RunSafeAsync(() => item is null
        ? Task.CompletedTask
        : Navigation.GoToAsync(Routes.GameDetail, new Dictionary<string, object> { [Routes.GameId] = item.GameId }));

    [RelayCommand]
    private Task CopyUpiAsync() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(UpiId)) return;
        await _clipboard.SetTextAsync(UpiId);
        Snackbar.Show($"Copied {Name}'s UPI ID");
    });

    /// <summary>Phase 5: merge a duplicate into this player (pick the duplicate from a list).</summary>
    [RelayCommand]
    private Task MergeAsync() => RunSafeAsync(async () =>
    {
        if (_player is null) return;
        var others = (await Data.GetPlayersAsync()).Select(p => p.Player).Where(p => p.Id != _player.Id).ToList();
        if (others.Count == 0)
        {
            Snackbar.Show("There's no other player to merge.");
            return;
        }
        var close = PlayerNames.CloseMatches(_player.Name, others, p => p.Name, max: 5);
        var choices = close.Concat(others.Except(close)).Select(p => p.Name).ToList();
        var chosen = await Dialogs.ChooseAsync($"Merge which player into {_player.Name}?", "Cancel", choices);
        var source = others.FirstOrDefault(p => p.Name == chosen);
        if (source is null) return;
        if (!await Dialogs.ConfirmAsync($"Merge {source.Name} into {_player.Name}?",
                $"All of {source.Name}'s games and payments move to {_player.Name}, and {source.Name} is deleted. This can't be undone.",
                "Merge", "Cancel")) return;
        try
        {
            await Data.MergePlayersAsync(source.Id, _player.Id);
        }
        catch (InvalidOperationException ex)
        {
            await Dialogs.AlertAsync("Can't merge", ex.Message);
            return;
        }
        Messenger.Send(new DataChangedMessage(DataArea.Players | DataArea.Results | DataArea.Games | DataArea.Payments));
        Snackbar.Show($"{source.Name} merged into {_player.Name}");
        await LoadAsync();
    });
}
