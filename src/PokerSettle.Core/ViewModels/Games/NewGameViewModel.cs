using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Services;
using PokerSettle.Core.ViewModels.Players;

namespace PokerSettle.Core.ViewModels.Games;

/// <summary>
/// New game on one screen: title (defaults to the date), date, default buy-in (from the last game),
/// location, players with zero-typing suggestions, "everyone starts with 1 buy-in", optional chip mode.
/// </summary>
public sealed partial class NewGameViewModel : ViewModelBase, IQueryReceiver
{
    private long? _fromGameId;
    private bool _loaded;
    private bool _settingTitle;
    private bool _copySettlement;
    private GameSettings _defaults = new();

    public NewGameViewModel(CoreServices services) : base(services)
    {
        Picker = new PlayerPickerViewModel(Data, Messenger, Snackbar, Log);
        Picker.SelectionChanged += (_, _) =>
        {
            PlayersError = null;
            UpdateStartText();
        };
        Date = Clock.Today.ToDateTime(TimeOnly.MinValue);
    }

    public PlayerPickerViewModel Picker { get; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string? TitleError { get; set; }

    [ObservableProperty]
    public partial DateTime? Date { get; set; }

    [ObservableProperty]
    public partial string? Location { get; set; }

    [ObservableProperty]
    public partial string BuyInText { get; set; } = "";

    [ObservableProperty]
    public partial string? BuyInError { get; set; }

    [ObservableProperty]
    public partial string? BuyInHint { get; set; }

    [ObservableProperty]
    public partial string BuyInLabel { get; set; } = "Default buy-in";

    [ObservableProperty]
    public partial bool StartWithBuyIn { get; set; } = true;

    [ObservableProperty]
    public partial bool ChipMode { get; set; }

    [ObservableProperty]
    public partial string ChipRateChipsText { get; set; } = "1000";

    [ObservableProperty]
    public partial string ChipRateMoneyText { get; set; } = "";

    [ObservableProperty]
    public partial string? ChipRateError { get; set; }

    [ObservableProperty]
    public partial string ChipRateSymbol { get; set; } = "₹";

    [ObservableProperty]
    public partial string? PlayersError { get; set; }

    [ObservableProperty]
    public partial string StartText { get; set; } = "Start game";

    [ObservableProperty]
    public partial bool HasOptions { get; set; }

    public void ApplyQuery(IDictionary<string, object> query) => _fromGameId = QueryLong(query, Routes.FromGameId);

    protected override async Task OnAppearingCoreAsync()
    {
        if (await Data.GetInProgressGameIdAsync() is { } running)
        {
            Snackbar.Show("A game is already in progress. Finish it before starting a new one.");
            await Navigation.GoToAsync($"../{Routes.LiveGame}", new Dictionary<string, object> { [Routes.GameId] = running });
            return;
        }
        if (_loaded) return;
        _loaded = true;

        ChipRateSymbol = Money.CurrencySymbol;
        GameSnapshot? template = _fromGameId is { } id ? await Data.GetGameAsync(id) : null;
        _copySettlement = template is not null;
        _defaults = template?.Game.Settings ?? await Data.GetLastGameSettingsAsync() ?? new GameSettings
        {
            DefaultBuyIn = Settings.DefaultBuyIn,
            SettlementMode = Settings.DefaultSettlementMode,
            Rounding = Settings.DefaultRounding,
        };

        ChipMode = template?.Game.Settings.ChipMode ?? false;
        if (_defaults.ChipRate is { IsValid: true } rate)
        {
            ChipRateChipsText = rate.Chips.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ChipRateMoneyText = Money.FormatForInput(rate.Money);
        }
        else
        {
            ChipRateMoneyText = Money.FormatForInput(Settings.DefaultBuyIn);
        }
        StartWithBuyIn = _defaults.StartWithBuyIn;
        // The last game's buy-in only makes sense in the same unit (money vs chips).
        var buyIn = _defaults.ChipMode == ChipMode ? _defaults.DefaultBuyIn : Settings.DefaultBuyIn;
        BuyInText = new GameMoney(Money, new GameSettings { ChipMode = ChipMode }).ForInput(buyIn);
        if (template is not null) Location = template.Game.Location;
        SetAutoTitle();

        var preselected = template?.Entries.Where(e => !e.Player.IsArchived).Select(e => e.PlayerId);
        await Picker.LoadAsync(excludeGameId: null, preselected: preselected);
        UpdateBuyInHint();
        UpdateStartText();
    }

    partial void OnDateChanged(DateTime? value)
    {
        if (!_settingTitle && value is not null && IsAutoTitle) SetAutoTitle();
    }

    partial void OnTitleChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) TitleError = null;
    }

    partial void OnBuyInTextChanged(string value)
    {
        BuyInError = null;
        UpdateBuyInHint();
    }

    partial void OnChipModeChanged(bool value)
    {
        BuyInLabel = value ? "Default buy-in (chips)" : "Default buy-in";
        if (!_loaded) return;
        // Switch the buy-in to the new unit: chips equal to one default money buy-in, or back.
        if (value)
        {
            var rate = ParseRate();
            var chips = rate is { } r ? (long)Math.Round((decimal)Settings.DefaultBuyIn * r.Chips / r.Money) : 1000;
            BuyInText = chips.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            BuyInText = Money.FormatForInput(Settings.DefaultBuyIn);
        }
        UpdateBuyInHint();
    }

    partial void OnChipRateChipsTextChanged(string value) => UpdateBuyInHint();

    partial void OnChipRateMoneyTextChanged(string value) => UpdateBuyInHint();

    private bool IsAutoTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title)) return true;
            // Still one of our generated titles (for any date)?
            return DateTime.TryParseExact(Title, "ddd, d MMM", Money.Culture, System.Globalization.DateTimeStyles.None, out _);
        }
    }

    private void SetAutoTitle()
    {
        _settingTitle = true;
        try
        {
            Title = Money.DefaultGameTitle(DateOnly.FromDateTime(Date ?? Clock.Today.ToDateTime(TimeOnly.MinValue)));
        }
        finally
        {
            _settingTitle = false;
        }
    }

    private ChipRate? ParseRate()
    {
        var chips = MoneyParser.ParseChips(ChipRateChipsText, Money, allowZero: false);
        var money = MoneyParser.ParseMoney(ChipRateMoneyText, Money, allowZero: false);
        return chips.Success && money.Success ? new ChipRate(chips.Value, money.Value) : null;
    }

    private void UpdateBuyInHint()
    {
        ChipRateError = null;
        if (!ChipMode)
        {
            BuyInHint = null;
            return;
        }
        var rate = ParseRate();
        var chips = MoneyParser.ParseChips(BuyInText, Money, allowZero: false);
        BuyInHint = rate is { } r && chips.Success ? $"= {Money.Format(MoneyFormatter.ChipsToMoney(chips.Value, r))} each" : null;
    }

    private void UpdateStartText()
    {
        var count = Picker.Selected.Count;
        StartText = count >= 2 ? $"Start game with {count} players" : "Start game";
    }

    [RelayCommand]
    private Task StartAsync() => RunSafeAsync(async () =>
    {
        var ok = true;
        var title = Title?.Trim() ?? "";
        if (title.Length == 0)
        {
            TitleError = "Give the game a title";
            ok = false;
        }

        ChipRate? rate = null;
        if (ChipMode)
        {
            rate = ParseRate();
            if (rate is null)
            {
                ChipRateError = "Enter how many chips are worth how much, e.g. 1000 chips = 500";
                ok = false;
            }
        }

        var buyIn = MoneyParser.ParseUnits(BuyInText, Money, ChipMode, allowZero: false);
        if (!buyIn.Success)
        {
            BuyInError = buyIn.Error;
            ok = false;
        }

        if (Picker.Selected.Count < 2)
        {
            PlayersError = "Pick at least 2 players";
            ok = false;
        }

        if (!ok)
        {
            Log.ValidationFailed("new game", TitleError ?? ChipRateError ?? BuyInError ?? PlayersError ?? "invalid");
            return;
        }

        // Settlement mode and rounding come from Settings, unless this copies another game's settings.
        var copyBanker = _copySettlement && _defaults.BankerId is { } banker && Picker.SelectedIds.Contains(banker);
        var settings = new GameSettings
        {
            DefaultBuyIn = buyIn.Value,
            ChipMode = ChipMode,
            ChipRate = rate,
            SettlementMode = _copySettlement ? _defaults.SettlementMode : Settings.DefaultSettlementMode,
            BankerId = copyBanker ? _defaults.BankerId : null,
            Rounding = _copySettlement ? _defaults.Rounding : Settings.DefaultRounding,
            StartWithBuyIn = StartWithBuyIn,
        };
        var date = DateOnly.FromDateTime(Date ?? Clock.Today.ToDateTime(TimeOnly.MinValue));
        long gameId;
        try
        {
            gameId = await Data.CreateGameAsync(new NewGameRequest(title, date, Location, null, settings, Picker.SelectedIds));
        }
        catch (GameInProgressException ex)
        {
            Snackbar.Show(ex.Message);
            await Navigation.GoToAsync($"../{Routes.LiveGame}", new Dictionary<string, object> { [Routes.GameId] = ex.GameId });
            return;
        }
        Messenger.Send(new DataChangedMessage(DataArea.Games, gameId));
        await Navigation.GoToAsync($"../{Routes.LiveGame}", new Dictionary<string, object> { [Routes.GameId] = gameId });
    });
}
