using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Money;
using PokerSettle.Core.Players;
using PokerSettle.Core.Services;
using PokerSettle.Core.Stats;

namespace PokerSettle.Core.ViewModels.Board;

public sealed class BoardItem
{
    public required int Rank { get; init; }
    public required long PlayerId { get; init; }
    public required string Name { get; init; }
    public required string Initials { get; init; }
    public required string Color { get; init; }
    public bool IsArchived { get; init; }
    public required string NetText { get; init; }
    public required int NetSign { get; init; }
    public required string WonText { get; init; }
    public required string LostText { get; init; }
    public required string GamesText { get; init; }
    public required string WinRateText { get; init; }
    public required string AverageText { get; init; }
    public required int AverageSign { get; init; }
    public required string BestText { get; init; }
    public required string WorstText { get; init; }
    public required string LastPlayedText { get; init; }
    public required ICommand OpenCommand { get; init; }

    public string RankText => $"#{Rank}";
    public string CardDetails => $"{GamesText} games · {WinRateText} winning nights · avg {AverageText}";
    public string CardExtremes => $"Best {BestText} · Worst {WorstText} · Last {LastPlayedText}";
    public string Description => $"Rank {Rank}, {Name}{(IsArchived ? " (archived)" : "")}, net {NetText}. {CardDetails}. {CardExtremes}.";

    internal static BoardItem From(RankedBoardRow ranked, MoneyFormatter money, ICommand open)
    {
        var r = ranked.Row;
        return new BoardItem
        {
            Rank = ranked.Rank,
            PlayerId = r.PlayerId,
            Name = r.IsArchived ? $"{r.Name} (archived)" : r.Name,
            Initials = PlayerNames.Initials(r.Name),
            Color = r.Color,
            IsArchived = r.IsArchived,
            NetText = money.FormatSigned(r.Net),
            NetSign = Math.Sign(r.Net),
            WonText = money.Format(r.Won),
            LostText = money.FormatSigned(r.Lost),
            GamesText = r.Games.ToString(money.Culture),
            WinRateText = money.FormatPercent(r.WinRate),
            AverageText = money.FormatSigned(r.Average),
            AverageSign = Math.Sign(r.Average),
            BestText = r.BiggestWin is { } best ? money.FormatSigned(best) : "—",
            WorstText = r.BiggestLoss is { } worst ? money.FormatSigned(worst) : "—",
            LastPlayedText = r.LastPlayed is { } last ? money.FormatShortDate(last) : "—",
            OpenCommand = open,
        };
    }
}

/// <summary>
/// The board: per-player totals over finalized games, sortable by any column, filtered by time and
/// archived status; sort and filter are remembered. Recomputes only when results actually changed.
/// </summary>
public sealed partial class BoardViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> FilterLabels =
        [.. Enum.GetValues<TimeFilterKind>().Select(TimeFilters.Label)];

    public static readonly IReadOnlyList<(BoardColumn Column, string Label)> SortOptions =
    [
        (BoardColumn.Net, "Net total"),
        (BoardColumn.Won, "Total won"),
        (BoardColumn.Lost, "Total lost"),
        (BoardColumn.Games, "Games played"),
        (BoardColumn.WinRate, "Win rate"),
        (BoardColumn.Average, "Average per game"),
        (BoardColumn.BiggestWin, "Biggest win"),
        (BoardColumn.BiggestLoss, "Biggest loss"),
        (BoardColumn.LastPlayed, "Last played"),
        (BoardColumn.Name, "Name"),
    ];

    private IReadOnlyList<RankedBoardRow> _ranked = [];
    private bool _restoring;

    public BoardViewModel(CoreServices services) : base(services)
    {
        _restoring = true;
        FilterIndex = (int)Settings.BoardFilter;
        CustomFrom = (Settings.BoardFrom ?? Clock.Today.AddMonths(-1)).ToDateTime(TimeOnly.MinValue);
        CustomTo = (Settings.BoardTo ?? Clock.Today).ToDateTime(TimeOnly.MinValue);
        IncludeArchived = Settings.BoardIncludeArchived;
        SortColumn = Settings.BoardSort;
        SortDescending = Settings.BoardSortDescending;
        SortIndex = Math.Max(0, SortOptions.ToList().FindIndex(o => o.Column == SortColumn));
        _restoring = false;
        UpdateHeaders();

        Messenger.Register<BoardViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            // Only finalized results (and names/archive flags) affect the board.
            if (!m.Affects(DataArea.Results | DataArea.Players)) return;
            r.IsStale = true;
            if (r.IsActive) _ = r.RunSafeAsync(r.LoadAsync, "Refresh");
        });
        Messenger.Register<BoardViewModel, SettingsChangedMessage>(this, static (r, m) =>
        {
            if (m.Key is "currency" or "culture") r.IsStale = true;
        });
    }

    public IReadOnlyList<string> Filters => FilterLabels;

    public IReadOnlyList<string> SortLabels { get; } = SortOptions.Select(o => o.Label).ToList();

    public ObservableCollection<BoardItem> Rows { get; } = [];

    /// <summary>How many times the board has been computed (lets tests check it doesn't recompute needlessly).</summary>
    public int LoadCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom))]
    public partial int FilterIndex { get; set; }

    [ObservableProperty]
    public partial DateTime? CustomFrom { get; set; }

    [ObservableProperty]
    public partial DateTime? CustomTo { get; set; }

    [ObservableProperty]
    public partial bool IncludeArchived { get; set; }

    [ObservableProperty]
    public partial BoardColumn SortColumn { get; set; }

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = "";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial string DirectionText { get; set; } = "";

    // Column header texts with the sort arrow (wide table).
    [ObservableProperty] public partial string NameHeader { get; set; } = "";
    [ObservableProperty] public partial string NetHeader { get; set; } = "";
    [ObservableProperty] public partial string WonHeader { get; set; } = "";
    [ObservableProperty] public partial string LostHeader { get; set; } = "";
    [ObservableProperty] public partial string GamesHeader { get; set; } = "";
    [ObservableProperty] public partial string WinRateHeader { get; set; } = "";
    [ObservableProperty] public partial string AverageHeader { get; set; } = "";
    [ObservableProperty] public partial string BestHeader { get; set; } = "";
    [ObservableProperty] public partial string WorstHeader { get; set; } = "";
    [ObservableProperty] public partial string LastHeader { get; set; } = "";

    public bool IsCustom => FilterIndex == (int)TimeFilterKind.Custom;

    protected override async Task OnAppearingCoreAsync()
    {
        if (IsStale) await LoadAsync();
    }

    partial void OnFilterIndexChanged(int value)
    {
        if (_restoring || value < 0) return;
        Settings.BoardFilter = (TimeFilterKind)value;
        _ = RunSafeAsync(LoadAsync, "Filter");
    }

    partial void OnCustomFromChanged(DateTime? value)
    {
        if (_restoring || value is null) return;
        Settings.BoardFrom = DateOnly.FromDateTime(value.Value);
        if (IsCustom) _ = RunSafeAsync(LoadAsync, "Filter");
    }

    partial void OnCustomToChanged(DateTime? value)
    {
        if (_restoring || value is null) return;
        Settings.BoardTo = DateOnly.FromDateTime(value.Value);
        if (IsCustom) _ = RunSafeAsync(LoadAsync, "Filter");
    }

    partial void OnIncludeArchivedChanged(bool value)
    {
        if (_restoring) return;
        Settings.BoardIncludeArchived = value;
        _ = RunSafeAsync(LoadAsync, "Filter");
    }

    partial void OnSortIndexChanged(int value)
    {
        if (_restoring || value < 0 || value >= SortOptions.Count) return;
        var column = SortOptions[value].Column;
        if (column == SortColumn) return;
        ApplySort(column, BoardMath.DefaultDescending(column));
    }

    public DateRange CurrentRange() => TimeFilters.Resolve((TimeFilterKind)Math.Max(0, FilterIndex), Clock.Today,
        CustomFrom is { } f ? DateOnly.FromDateTime(f) : null, CustomTo is { } t ? DateOnly.FromDateTime(t) : null);

    private async Task LoadAsync()
    {
        var rows = await Data.GetBoardAsync(CurrentRange(), IncludeArchived);
        IsStale = false;
        LoadCount++;
        _ranked = BoardMath.RankByNet(rows);
        Present();
        Summary = rows.Count switch
        {
            0 => "",
            1 => "1 player",
            _ => $"{rows.Count} players",
        };
        IsEmpty = rows.Count == 0;
        EmptyText = FilterIndex == (int)TimeFilterKind.AllTime && !IncludeArchived
            ? "No finished games yet. Finalize a game and the board fills in."
            : "No finished games in this period.";
    }

    private void Present()
    {
        var sorted = BoardMath.Sort(_ranked, SortColumn, SortDescending);
        Rows.Clear();
        foreach (var row in sorted) Rows.Add(BoardItem.From(row, Money, OpenPlayerCommand));
    }

    /// <summary>Header tap: sort by that column, or flip the direction if it's already sorted by it.</summary>
    [RelayCommand]
    private void SortBy(string? columnName)
    {
        if (!Enum.TryParse<BoardColumn>(columnName, out var column)) return;
        var descending = column == SortColumn ? !SortDescending : BoardMath.DefaultDescending(column);
        ApplySort(column, descending);
    }

    [RelayCommand]
    private void ToggleDirection() => ApplySort(SortColumn, !SortDescending);

    private void ApplySort(BoardColumn column, bool descending)
    {
        SortColumn = column;
        SortDescending = descending;
        Settings.BoardSort = column;
        Settings.BoardSortDescending = descending;
        _restoring = true;
        SortIndex = Math.Max(0, SortOptions.ToList().FindIndex(o => o.Column == column));
        _restoring = false;
        UpdateHeaders();
        Present();
    }

    private void UpdateHeaders()
    {
        string H(BoardColumn c, string label) => c == SortColumn ? $"{label} {(SortDescending ? "▼" : "▲")}" : label;
        NameHeader = H(BoardColumn.Name, "Player");
        NetHeader = H(BoardColumn.Net, "Net");
        WonHeader = H(BoardColumn.Won, "Won");
        LostHeader = H(BoardColumn.Lost, "Lost");
        GamesHeader = H(BoardColumn.Games, "Games");
        WinRateHeader = H(BoardColumn.WinRate, "Win %");
        AverageHeader = H(BoardColumn.Average, "Avg");
        BestHeader = H(BoardColumn.BiggestWin, "Best");
        WorstHeader = H(BoardColumn.BiggestLoss, "Worst");
        LastHeader = H(BoardColumn.LastPlayed, "Last played");
        DirectionText = SortDescending ? "▼ High to low" : "▲ Low to high";
        if (SortColumn is BoardColumn.Name) DirectionText = SortDescending ? "▼ Z to A" : "▲ A to Z";
        if (SortColumn is BoardColumn.LastPlayed) DirectionText = SortDescending ? "▼ Newest first" : "▲ Oldest first";
    }

    [RelayCommand]
    private Task OpenPlayerAsync(BoardItem? item) => RunSafeAsync(() => item is null
        ? Task.CompletedTask
        : Navigation.GoToAsync(Routes.PlayerDetail, new Dictionary<string, object> { [Routes.PlayerId] = item.PlayerId }));
}
