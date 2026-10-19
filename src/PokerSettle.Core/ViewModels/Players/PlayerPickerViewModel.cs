using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Players;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels.Players;

/// <summary>A tappable player chip.</summary>
public sealed partial class PickChip : ObservableObject
{
    public required long PlayerId { get; init; }
    public required string Name { get; init; }
    public string? Nickname { get; init; }
    public required string Initials { get; init; }
    public required string Color { get; init; }
    public required IRelayCommand<PickChip> ToggleCommand { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(Description))]
    public partial bool IsSelected { get; set; }

    public string Label => IsSelected ? $"✓ {Name}" : Name;
    public string Description => IsSelected ? $"Remove {Name}" : $"Add {Name}";
}

/// <summary>
/// Picks players with zero typing for regulars: "Same players as last game", ranked suggestion chips,
/// search, and "Add 'Name' as a new player" when nothing matches. Used by New game and late players.
/// </summary>
public sealed partial class PlayerPickerViewModel : ObservableObject
{
    public const int SuggestionCount = 12;
    public const int SearchResultCount = 20;

    private readonly DataStore _data;
    private readonly IMessenger _messenger;
    private readonly SnackbarService _snackbar;
    private readonly ILogger _log;
    private readonly Dictionary<long, PickChip> _chips = [];
    private List<long> _ranked = [];
    private List<long> _lastGame = [];
    private HashSet<long> _locked = [];

    public PlayerPickerViewModel(DataStore data, IMessenger messenger, SnackbarService snackbar, ILogger logger)
    {
        _data = data;
        _messenger = messenger;
        _snackbar = snackbar;
        _log = logger;
    }

    /// <summary>Raised whenever the selection changes.</summary>
    public event EventHandler? SelectionChanged;

    public ObservableCollection<PickChip> Selected { get; } = [];
    public ObservableCollection<PickChip> Suggestions { get; } = [];
    public ObservableCollection<PickChip> SearchResults { get; } = [];

    public IReadOnlyList<long> SelectedIds => Selected.Select(c => c.PlayerId).ToList();

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial bool CanAddNew { get; set; }

    [ObservableProperty]
    public partial string AddNewText { get; set; } = "";

    [ObservableProperty]
    public partial string? DidYouMean { get; set; }

    [ObservableProperty]
    public partial string? AddNewError { get; set; }

    [ObservableProperty]
    public partial bool HasLastGame { get; set; }

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial bool HasSuggestions { get; set; }

    [ObservableProperty]
    public partial string SelectionSummary { get; set; } = "Pick at least 2 players";

    [ObservableProperty]
    public partial bool NoPlayersYet { get; set; }

    /// <param name="excludeGameId">The current game (late players), left out of "last game" signals.</param>
    /// <param name="locked">Players already in the game: never suggested.</param>
    /// <param name="preselected">Players to select initially (e.g. "same players and settings").</param>
    public async Task LoadAsync(long? excludeGameId, IEnumerable<long>? locked = null, IEnumerable<long>? preselected = null)
    {
        _locked = locked?.ToHashSet() ?? [];
        var candidates = SuggestionRanker.Rank(await _data.GetSuggestionCandidatesAsync(excludeGameId));
        _lastGame = (await _data.GetLastGamePlayerIdsAsync(excludeGameId)).Where(id => !_locked.Contains(id)).ToList();

        var keep = Selected.Select(c => c.PlayerId).ToList();
        _chips.Clear();
        foreach (var c in candidates.Where(c => !_locked.Contains(c.PlayerId)))
        {
            _chips[c.PlayerId] = new PickChip
            {
                PlayerId = c.PlayerId,
                Name = c.Name,
                Nickname = c.Nickname,
                Initials = PlayerNames.Initials(c.Name),
                Color = c.Color,
                ToggleCommand = ToggleCommand,
            };
        }
        _ranked = candidates.Select(c => c.PlayerId).Where(_chips.ContainsKey).ToList();

        Selected.Clear();
        foreach (var id in (preselected ?? keep).Distinct())
        {
            if (_chips.TryGetValue(id, out var chip))
            {
                chip.IsSelected = true;
                Selected.Add(chip);
            }
        }
        HasLastGame = _lastGame.Count > 0;
        NoPlayersYet = _chips.Count == 0;
        Refresh();
    }

    partial void OnSearchTextChanged(string? value) => RefreshSearch();

    [RelayCommand]
    private void Toggle(PickChip? chip)
    {
        if (chip is null) return;
        if (chip.IsSelected)
        {
            chip.IsSelected = false;
            Selected.Remove(chip);
        }
        else
        {
            chip.IsSelected = true;
            Selected.Add(chip);
            if (!string.IsNullOrEmpty(SearchText)) SearchText = string.Empty;
        }
        Refresh();
    }

    [RelayCommand]
    private void SameAsLastGame()
    {
        foreach (var id in _lastGame)
        {
            if (_chips.TryGetValue(id, out var chip) && !chip.IsSelected)
            {
                chip.IsSelected = true;
                Selected.Add(chip);
            }
        }
        Refresh();
    }

    /// <summary>Enter in the search box: pick the only match, or add the typed name as a new player.</summary>
    [RelayCommand]
    private async Task SubmitSearchAsync()
    {
        var unselected = SearchResults.Where(c => !c.IsSelected).ToList();
        if (SearchResults.Count == 1 && unselected.Count == 1) Toggle(unselected[0]);
        else if (CanAddNew) await AddNewAsync();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var chip in Selected) chip.IsSelected = false;
        Selected.Clear();
        Refresh();
    }

    /// <summary>Creates a player from the search text and selects them (or selects the existing one).</summary>
    [RelayCommand]
    private async Task AddNewAsync()
    {
        var name = PlayerNames.Clean(SearchText);
        if (name.Length == 0) return;
        if (name.Length > PlayerNames.MaxNameLength)
        {
            AddNewError = $"Keep names under {PlayerNames.MaxNameLength} characters";
            return;
        }
        try
        {
            var existing = _chips.Values.FirstOrDefault(c => PlayerNames.Key(c.Name) == PlayerNames.Key(name));
            if (existing is not null)
            {
                if (!existing.IsSelected) Toggle(existing);
                return;
            }
            Models.Player player;
            try
            {
                player = await _data.AddPlayerAsync(new PlayerDraft(name, null, null, null, null, null));
            }
            catch (DuplicatePlayerNameException ex)
            {
                // The name exists but isn't offered here: already in this game, or archived.
                var other = await _data.GetPlayerAsync(ex.ExistingPlayerId);
                AddNewError = other is not null && _locked.Contains(other.Id)
                    ? $"{other.Name} is already in this game."
                    : $"{other?.Name ?? name} is archived. Unarchive them in Players to add them again.";
                _log.ValidationFailed("player picker", "duplicate name");
                return;
            }
            var chip = new PickChip
            {
                PlayerId = player.Id,
                Name = player.Name,
                Initials = PlayerNames.Initials(player.Name),
                Color = player.Color,
                ToggleCommand = ToggleCommand,
                IsSelected = true,
            };
            _chips[player.Id] = chip;
            _ranked.Add(player.Id);
            Selected.Add(chip);
            SearchText = string.Empty;
            NoPlayersYet = false;
            _messenger.Send(new DataChangedMessage(DataArea.Players));
            _snackbar.Show($"{player.Name} saved to Players");
            Refresh();
        }
        catch (Exception ex)
        {
            _log.CommandFailed(ex, "PlayerPicker.AddNew");
            _snackbar.ShowError();
        }
    }

    private void Refresh()
    {
        Suggestions.Clear();
        foreach (var id in _ranked)
        {
            if (Suggestions.Count >= SuggestionCount) break;
            var chip = _chips[id];
            if (!chip.IsSelected) Suggestions.Add(chip);
        }
        HasSuggestions = Suggestions.Count > 0;
        HasSelection = Selected.Count > 0;
        SelectionSummary = Selected.Count switch
        {
            0 => "Pick at least 2 players",
            1 => "1 player picked",
            _ => $"{Selected.Count} players picked",
        };
        RefreshSearch();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshSearch()
    {
        AddNewError = null;
        var text = PlayerNames.Clean(SearchText);
        IsSearching = text.Length > 0;
        SearchResults.Clear();
        DidYouMean = null;
        if (!IsSearching)
        {
            CanAddNew = false;
            return;
        }

        var key = PlayerNames.Key(text);
        var matches = _ranked
            .Select(id => _chips[id])
            .Where(c => PlayerNames.Key(c.Name + " " + c.Nickname).Contains(key, StringComparison.Ordinal))
            .Take(SearchResultCount)
            .ToList();
        var exact = _chips.Values.Any(c => PlayerNames.Key(c.Name) == key);
        if (matches.Count == 0)
        {
            var close = PlayerNames.CloseMatches(text, _chips.Values, c => c.Name, max: 3);
            if (close.Count > 0) DidYouMean = "Did you mean " + string.Join(" or ", close.Select(c => c.Name)) + "?";
            matches.AddRange(close);
        }
        foreach (var chip in matches) SearchResults.Add(chip);
        CanAddNew = !exact;
        AddNewText = $"Add \"{text}\" as a new player";
    }
}
