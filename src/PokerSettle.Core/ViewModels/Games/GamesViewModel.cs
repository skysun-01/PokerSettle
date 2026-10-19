using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels.Games;

public sealed class GameRow
{
    public required long Id { get; init; }
    public required string Title { get; init; }
    public required string DateText { get; init; }
    public required string Subtitle { get; init; }
    public string? TopWinnerText { get; init; }
    public bool IsInProgress { get; init; }
    public bool HasUnpaid { get; init; }
    public string? UnpaidText { get; init; }
    public required ICommand OpenCommand { get; init; }

    public bool HasTopWinner => TopWinnerText is not null;
    public string Description => $"{Title}. {Subtitle}." + (IsInProgress ? " In progress." : "")
        + (TopWinnerText is null ? "" : $" {TopWinnerText}.") + (HasUnpaid ? $" {UnpaidText}." : "");

    internal static GameRow From(GameListItem g, MoneyFormatter money, ICommand open)
    {
        var units = new GameMoney(money, new GameSettings { ChipMode = g.ChipMode, ChipRate = g.ChipRate });
        var players = g.PlayerCount == 1 ? "1 player" : $"{g.PlayerCount} players";
        return new GameRow
        {
            Id = g.Id,
            Title = g.Title,
            DateText = money.FormatDate(g.Date),
            Subtitle = $"{money.FormatCompactDate(g.Date)} · {players} · pot {units.UnitsWithMoney(g.Pot)}",
            TopWinnerText = g.TopWinnerName is { } name && g.TopWinnerNet is { } net ? $"Top: {name} {money.FormatSigned(net)}" : null,
            IsInProgress = g.Status == GameStatus.InProgress,
            HasUnpaid = g.UnpaidCount > 0,
            UnpaidText = g.UnpaidCount == 1 ? "1 unpaid" : $"{g.UnpaidCount} unpaid",
            OpenCommand = open,
        };
    }
}

/// <summary>Games history: newest first, search by title or player, date range filter, badges.</summary>
public sealed partial class GamesViewModel : ViewModelBase
{
    private CancellationTokenSource? _searchDelay;
    private bool _suppressReload;

    public GamesViewModel(CoreServices services) : base(services)
    {
        var today = Clock.Today.ToDateTime(TimeOnly.MinValue);
        _suppressReload = true;
        FromDate = today.AddMonths(-3);
        ToDate = today;
        _suppressReload = false;
        Messenger.Register<GamesViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (!m.Affects(DataArea.Games | DataArea.Results | DataArea.Payments | DataArea.Players)) return;
            r.IsStale = true;
            if (r.IsActive) _ = r.RunSafeAsync(r.LoadAsync, "Refresh");
        });
        Messenger.Register<GamesViewModel, SettingsChangedMessage>(this, static (r, _) => r.IsStale = true);
    }

    public ObservableCollection<GameRow> Items { get; } = [];

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool FilterByDate { get; set; }

    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasNoGamesAtAll { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = "";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    partial void OnSearchTextChanged(string? value) => ReloadSoon();

    partial void OnFilterByDateChanged(bool value) => ReloadNow();

    partial void OnFromDateChanged(DateTime? value)
    {
        if (FilterByDate) ReloadNow();
    }

    partial void OnToDateChanged(DateTime? value)
    {
        if (FilterByDate) ReloadNow();
    }

    protected override async Task OnAppearingCoreAsync()
    {
        if (IsStale) await LoadAsync();
    }

    private void ReloadNow()
    {
        if (_suppressReload) return;
        _ = RunSafeAsync(LoadAsync, "Filter");
    }

    /// <summary>Debounced search so typing doesn't query on every keystroke.</summary>
    private void ReloadSoon()
    {
        _searchDelay?.Cancel();
        if (_suppressReload) return;
        var cts = new CancellationTokenSource();
        _searchDelay = cts;
        _ = RunSafeAsync(async () =>
        {
            await Task.Delay(250, cts.Token);
            await LoadAsync();
        }, "Search");
    }

    private async Task LoadAsync()
    {
        DateOnly? from = FilterByDate && FromDate is { } f ? DateOnly.FromDateTime(f) : null;
        DateOnly? to = FilterByDate && ToDate is { } t ? DateOnly.FromDateTime(t) : null;
        if (from > to) (from, to) = (to, from);
        var search = SearchText?.Trim();
        var games = await Data.GetGamesAsync(new GameQuery(search, from, to));
        IsStale = false;

        Items.Clear();
        foreach (var g in games) Items.Add(GameRow.From(g, Money, OpenGameCommand));

        var filtered = !string.IsNullOrEmpty(search) || FilterByDate;
        HasNoGamesAtAll = games.Count == 0 && !filtered;
        IsEmpty = games.Count == 0;
        EmptyText = HasNoGamesAtAll
            ? "No games yet. Start your first one."
            : "No games match. Try a different search or dates.";
        Summary = games.Count switch
        {
            0 => "",
            1 => "1 game",
            _ => $"{games.Count} games",
        };
    }

    [RelayCommand]
    private Task OpenGameAsync(GameRow? row) => RunSafeAsync(() => row is null
        ? Task.CompletedTask
        : Navigation.GoToAsync(row.IsInProgress ? Routes.LiveGame : Routes.GameDetail,
            new Dictionary<string, object> { [Routes.GameId] = row.Id }));

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressReload = true;
        SearchText = null;
        FilterByDate = false;
        _suppressReload = false;
        ReloadNow();
    }
}
