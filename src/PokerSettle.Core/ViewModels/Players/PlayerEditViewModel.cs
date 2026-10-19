using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Players;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels.Players;

public sealed partial class ColorChoice : ObservableObject
{
    public required string Color { get; init; }
    public required IRelayCommand<ColorChoice> SelectCommand { get; init; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>Add or edit a player. Blocks duplicate names inline and suggests close matches.</summary>
public sealed partial class PlayerEditViewModel : ViewModelBase, IQueryReceiver
{
    private long? _playerId;
    private Player? _original;
    private List<Player> _others = [];
    private bool _colorChosen;
    private bool _loaded;

    public PlayerEditViewModel(CoreServices services) : base(services)
    {
        foreach (var color in PlayerNames.Palette)
            Colors.Add(new ColorChoice { Color = color, SelectCommand = SelectColorCommand });
    }

    public ObservableCollection<ColorChoice> Colors { get; } = [];

    [ObservableProperty]
    public partial string PageTitle { get; set; } = "Add player";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initials))]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    [ObservableProperty]
    public partial string? DidYouMean { get; set; }

    private long? _didYouMeanId;

    [ObservableProperty]
    public partial string? Nickname { get; set; }

    [ObservableProperty]
    public partial string? Phone { get; set; }

    [ObservableProperty]
    public partial string? PhoneError { get; set; }

    [ObservableProperty]
    public partial string? UpiId { get; set; }

    [ObservableProperty]
    public partial string? UpiError { get; set; }

    [ObservableProperty]
    public partial string? Notes { get; set; }

    [ObservableProperty]
    public partial string SelectedColor { get; set; } = PlayerNames.Palette[0];

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial bool IsArchived { get; set; }

    [ObservableProperty]
    public partial bool CanDelete { get; set; }

    [ObservableProperty]
    public partial string ManageHint { get; set; } = "";

    public string Initials => PlayerNames.Initials(Name);

    public void ApplyQuery(IDictionary<string, object> query)
    {
        _playerId = QueryLong(query, Routes.PlayerId);
        if (query.TryGetValue("name", out var name) && name is string n && _playerId is null) Name = n;
    }

    protected override async Task OnAppearingCoreAsync()
    {
        if (_loaded) return;
        _loaded = true;
        var players = await Data.GetPlayersAsync();
        _others = players.Select(p => p.Player).Where(p => p.Id != _playerId).ToList();

        if (_playerId is { } id)
        {
            _original = await Data.GetPlayerAsync(id);
            if (_original is null)
            {
                await Navigation.GoBackAsync();
                return;
            }
            IsEditing = true;
            PageTitle = "Edit player";
            Name = _original.Name;
            Nickname = _original.Nickname;
            Phone = _original.Phone;
            UpiId = _original.UpiId;
            Notes = _original.Notes;
            IsArchived = _original.IsArchived;
            _colorChosen = true;
            SetColor(_original.Color);
            var hasHistory = await Data.HasHistoryAsync(id);
            CanDelete = !hasHistory;
            ManageHint = hasHistory
                ? "Players with games can be archived: they're hidden from suggestions but their results stay on the board."
                : "This player has no games yet, so they can be deleted.";
            NameError = null;
            DidYouMean = null;
        }
        else
        {
            SetColor(PlayerNames.ColorFor(Name));
            Validate(showRequired: false);
        }
    }

    partial void OnNameChanged(string? value)
    {
        if (!_colorChosen) SetColor(PlayerNames.ColorFor(value));
        Validate(showRequired: false);
    }

    partial void OnPhoneChanged(string? value) => PhoneError = null;

    partial void OnUpiIdChanged(string? value) => UpiError = null;

    /// <returns>True when the form can be saved.</returns>
    private bool Validate(bool showRequired)
    {
        var name = PlayerNames.Clean(Name);
        DidYouMean = null;
        _didYouMeanId = null;
        if (name.Length == 0)
        {
            NameError = showRequired ? "Enter a name" : null;
            return false;
        }
        if (name.Length > PlayerNames.MaxNameLength)
        {
            NameError = $"Keep it under {PlayerNames.MaxNameLength} characters";
            return false;
        }
        var key = PlayerNames.Key(name);
        var duplicate = _others.FirstOrDefault(p => PlayerNames.Key(p.Name) == key);
        if (duplicate is not null)
        {
            NameError = duplicate.IsArchived
                ? $"{duplicate.Name} already exists (archived). Unarchive them instead."
                : $"{duplicate.Name} already exists.";
            return false;
        }
        NameError = null;
        var close = PlayerNames.CloseMatches(name, _others, p => p.Name, max: 1);
        if (close.Count > 0 && _original?.Name != name)
        {
            DidYouMean = $"Did you mean {close[0].Name}?";
            _didYouMeanId = close[0].Id;
        }
        return true;
    }

    private bool ValidateContact()
    {
        var ok = true;
        if (!string.IsNullOrWhiteSpace(Phone) && Phone.Any(c => !(char.IsDigit(c) || c is '+' or ' ' or '-' or '(' or ')')))
        {
            PhoneError = "Use digits, spaces, + or -";
            ok = false;
        }
        var upi = UpiId?.Trim();
        if (!string.IsNullOrEmpty(upi) && (upi.Contains(' ') || upi.Count(c => c == '@') != 1 || upi.StartsWith('@') || upi.EndsWith('@')))
        {
            UpiError = "A UPI ID looks like name@bank";
            ok = false;
        }
        return ok;
    }

    [RelayCommand]
    private void SelectColor(ColorChoice? choice)
    {
        if (choice is null) return;
        _colorChosen = true;
        SetColor(choice.Color);
    }

    private void SetColor(string color)
    {
        SelectedColor = color;
        foreach (var c in Colors) c.IsSelected = string.Equals(c.Color, color, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private Task SaveAsync() => RunSafeAsync(async () =>
    {
        var valid = Validate(showRequired: true);
        valid &= ValidateContact();
        if (!valid)
        {
            Log.ValidationFailed("player edit", NameError ?? PhoneError ?? UpiError ?? "invalid");
            return;
        }
        try
        {
            if (_original is null)
            {
                var player = await Data.AddPlayerAsync(new PlayerDraft(Name!, Nickname, Phone, UpiId?.Trim(), Notes, SelectedColor));
                Snackbar.Show($"{player.Name} added");
            }
            else
            {
                var updated = await Data.UpdatePlayerAsync(_original with
                {
                    Name = Name!,
                    Nickname = Nickname,
                    Phone = Phone,
                    UpiId = UpiId?.Trim(),
                    Notes = Notes,
                    Color = SelectedColor,
                });
                Snackbar.Show($"{updated.Name} saved");
            }
        }
        catch (DuplicatePlayerNameException ex)
        {
            NameError = ex.Message;
            return;
        }
        Messenger.Send(new DataChangedMessage(DataArea.Players | DataArea.Results));
        await Navigation.GoBackAsync();
    });

    [RelayCommand]
    private Task OpenDidYouMeanAsync() => RunSafeAsync(() => _didYouMeanId is { } id
        ? Navigation.GoToAsync($"../{Routes.PlayerDetail}", new Dictionary<string, object> { [Routes.PlayerId] = id })
        : Task.CompletedTask);

    [RelayCommand]
    private Task ToggleArchiveAsync() => RunSafeAsync(async () =>
    {
        if (_original is null) return;
        var archive = !IsArchived;
        await Data.SetArchivedAsync(_original.Id, archive);
        IsArchived = archive;
        _original = _original with { IsArchived = archive };
        Messenger.Send(new DataChangedMessage(DataArea.Players | DataArea.Results));
        var id = _original.Id;
        var name = _original.Name;
        Snackbar.ShowUndo(archive ? $"{name} archived" : $"{name} is back", async () =>
        {
            await Data.SetArchivedAsync(id, !archive);
            IsArchived = !archive;
            if (_original is not null) _original = _original with { IsArchived = !archive };
            Messenger.Send(new DataChangedMessage(DataArea.Players | DataArea.Results));
        });
    });

    [RelayCommand]
    private Task DeleteAsync() => RunSafeAsync(async () =>
    {
        if (_original is null || !CanDelete) return;
        if (!await Dialogs.ConfirmAsync($"Delete {_original.Name}?", "This can't be undone.", "Delete", "Cancel")) return;
        await Data.DeletePlayerAsync(_original.Id);
        Messenger.Send(new DataChangedMessage(DataArea.Players));
        Snackbar.Show($"{_original.Name} deleted");
        await Navigation.GoToAsync(Routes.Tab(Routes.Players));
    });
}
