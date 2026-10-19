using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Players;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels.Players;

public sealed class PlayerRow
{
    public required long Id { get; init; }
    public required string Name { get; init; }
    public string? Nickname { get; init; }
    public required string Initials { get; init; }
    public required string Color { get; init; }
    public bool IsArchived { get; init; }
    public required string Subtitle { get; init; }
    public required ICommand OpenCommand { get; init; }
    internal string SearchKey { get; init; } = "";

    public bool HasNickname => !string.IsNullOrWhiteSpace(Nickname);
}

/// <summary>Players tab: searchable list, archived toggle, add.</summary>
public sealed partial class PlayersViewModel : ViewModelBase
{
    private List<PlayerRow> _all = [];

    public PlayersViewModel(CoreServices services) : base(services)
    {
        ShowArchived = Settings.PlayersShowArchived;
        Messenger.Register<PlayersViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (m.Affects(DataArea.Players | DataArea.Results)) r.IsStale = true;
        });
        Messenger.Register<PlayersViewModel, SettingsChangedMessage>(this, static (r, _) => r.IsStale = true);
    }

    public ObservableCollection<PlayerRow> Items { get; } = [];

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool ShowArchived { get; set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    public partial bool HasNoPlayersAtAll { get; set; }

    [ObservableProperty]
    public partial string EmptyText { get; set; } = "";

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    partial void OnShowArchivedChanged(bool value)
    {
        Settings.PlayersShowArchived = value;
        ApplyFilter();
    }

    protected override async Task OnAppearingCoreAsync()
    {
        if (!IsStale) return;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var players = await Data.GetPlayersAsync();
        IsStale = false;
        _all = players.Select(ToRow).ToList();
        ApplyFilter();
    }

    private PlayerRow ToRow(PlayerListItem item)
    {
        var p = item.Player;
        var subtitle = item.GamesPlayed switch
        {
            0 => "No games yet",
            1 => "1 game",
            _ => $"{item.GamesPlayed} games",
        };
        if (item.LastPlayed is { } last) subtitle += $" · last played {Money.FormatShortDate(last)}";
        if (p.IsArchived) subtitle = "Archived · " + subtitle;
        return new PlayerRow
        {
            Id = p.Id,
            Name = p.Name,
            Nickname = p.Nickname,
            Initials = PlayerNames.Initials(p.Name),
            Color = p.Color,
            IsArchived = p.IsArchived,
            Subtitle = subtitle,
            OpenCommand = OpenPlayerCommand,
            SearchKey = PlayerNames.Key(p.Name + " " + p.Nickname),
        };
    }

    private void ApplyFilter()
    {
        var key = PlayerNames.Key(SearchText);
        var visible = _all.Where(r => (ShowArchived || !r.IsArchived) && (key.Length == 0 || r.SearchKey.Contains(key, StringComparison.Ordinal))).ToList();
        Items.Clear();
        foreach (var row in visible) Items.Add(row);

        var active = _all.Count(r => !r.IsArchived);
        var archived = _all.Count - active;
        Summary = archived > 0 ? $"{active} players · {archived} archived" : $"{active} players";
        HasNoPlayersAtAll = _all.Count == 0;
        IsEmpty = visible.Count == 0;
        EmptyText = _all.Count == 0
            ? "No players yet. Add your first one."
            : key.Length > 0 ? $"No player matches \"{SearchText?.Trim()}\"." : "Everyone here is archived. Turn on \"Show archived\" to see them.";
    }

    [RelayCommand]
    private Task AddPlayerAsync() => RunSafeAsync(() =>
    {
        var name = PlayerNames.Clean(SearchText);
        var query = new Dictionary<string, object>();
        if (name.Length > 0 && Items.Count == 0) query["name"] = name;
        return Navigation.GoToAsync(Routes.PlayerEdit, query);
    });

    [RelayCommand]
    private Task OpenPlayerAsync(PlayerRow? row) => RunSafeAsync(() =>
        row is null ? Task.CompletedTask : Navigation.GoToAsync(Routes.PlayerEdit, new Dictionary<string, object> { [Routes.PlayerId] = row.Id }));
}
