using System.Collections.ObjectModel;
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
/// Review before finalizing: results sorted by net and the payments, with settlement mode, banker and
/// rounding adjustable (recomputed instantly). Finalize stores everything in one transaction.
/// </summary>
public sealed partial class ReviewViewModel : ViewModelBase, IQueryReceiver
{
    public static readonly IReadOnlyList<string> ModeLabels = ["Fewest payments", "Banker"];

    /// <summary>Where Finalize goes: back past review, cash-out and the live game, to the game's details.</summary>
    public const string FinishedRoute = "../../../" + Routes.GameDetail;

    private long _gameId;
    private DiscrepancyResolution _resolution;
    private long? _assignedId;
    private GameSnapshot? _game;
    private GameMoney? _money;
    private bool _ready;

    public ReviewViewModel(CoreServices services) : base(services)
    {
    }

    public ObservableCollection<ResultRow> Results { get; } = [];
    public ObservableCollection<PaymentRow> Payments { get; } = [];
    public IReadOnlyList<string> Modes => ModeLabels;

    [ObservableProperty]
    public partial IReadOnlyList<string> RoundingLabels { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> PlayerNames { get; set; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "Review";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBanker))]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    public partial int BankerIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int RoundingIndex { get; set; }

    [ObservableProperty]
    public partial string PaymentsTitle { get; set; } = "Payments";

    [ObservableProperty]
    public partial string? AdjustmentText { get; set; }

    [ObservableProperty]
    public partial string? WarningText { get; set; }

    [ObservableProperty]
    public partial string? ErrorText { get; set; }

    [ObservableProperty]
    public partial string? NoPaymentsText { get; set; }

    [ObservableProperty]
    public partial string PotText { get; set; } = "";

    public bool IsBanker => ModeIndex == 1;

    public void ApplyQuery(IDictionary<string, object> query)
    {
        if (QueryLong(query, Routes.GameId) is { } id) _gameId = id;
        _resolution = (DiscrepancyResolution)(QueryLong(query, Routes.Resolution) ?? 0);
        _assignedId = QueryLong(query, Routes.AssignedPlayerId);
    }

    protected override async Task OnAppearingCoreAsync()
    {
        var game = await Data.GetGameAsync(_gameId);
        if (game is null || game.Game.IsFinalized)
        {
            await Navigation.GoBackAsync();
            return;
        }
        _ready = false;
        _game = game;
        _money = new GameMoney(Money, game.Game.Settings);
        Title = $"Review · {game.Game.Title}";
        PotText = $"Pot {_money.UnitsWithMoney(game.Pot)} · {game.Entries.Count} players";
        PlayerNames = game.Entries.Select(e => e.Player.Name).ToList();
        RoundingLabels = ["No rounding", $"Nearest {Money.Format(100)}", $"Nearest {Money.Format(1_000)}"];
        var settings = game.Game.Settings;
        var bankerIndex = settings.BankerId is { } banker ? game.Entries.ToList().FindIndex(e => e.PlayerId == banker) : -1;
        // Banker mode without a (present) banker: default to the first seat, usually the host.
        if (bankerIndex < 0 && settings.SettlementMode == SettlementMode.Banker) bankerIndex = 0;
        BankerIndex = bankerIndex;
        ModeIndex = (int)settings.SettlementMode;
        RoundingIndex = (int)settings.Rounding;
        var noBuyIns = game.Entries.Where(e => game.BuyInCount(e.PlayerId) == 0).Select(e => e.Player.Name).ToList();
        WarningText = noBuyIns.Count == 0 ? null : $"No buy-ins recorded for {string.Join(", ", noBuyIns)}. Check that's right.";
        _ready = true;
        Recompute();
    }

    partial void OnModeIndexChanged(int value)
    {
        if (value == 1 && BankerIndex < 0 && _game is { Entries.Count: > 0 })
        {
            // Default banker: the host is usually first in seat order.
            BankerIndex = 0;
        }
        Recompute();
    }

    partial void OnBankerIndexChanged(int value) => Recompute();

    partial void OnRoundingIndexChanged(int value) => Recompute();

    private SettlementOptions Options() => new(
        _game!.Game.Settings.ChipMode,
        _game.Game.Settings.ChipRate,
        (SettlementMode)Math.Max(0, ModeIndex),
        ModeIndex == 1 && BankerIndex >= 0 && BankerIndex < _game.Entries.Count ? _game.Entries[BankerIndex].PlayerId : null,
        (RoundingMode)Math.Max(0, RoundingIndex));

    private void Recompute()
    {
        if (!_ready || _game is null || _money is null) return;
        RunSafe(() =>
        {
            ErrorText = null;
            AdjustmentText = null;
            var lines = (IReadOnlyList<PlayerLine>)GameRowFactory.Lines(_game);
            var imbalance = SettlementEngine.Imbalance(lines);
            if (imbalance != 0 && _resolution != DiscrepancyResolution.None)
            {
                var shares = SettlementEngine.ResolveDiscrepancy(lines, _resolution, _assignedId);
                lines = SettlementEngine.ApplyAdjustments(lines, shares);
                var kind = _resolution == DiscrepancyResolution.SplitEqually ? AdjustmentKind.SplitDifference : AdjustmentKind.AssignedDifference;
                AdjustmentText = GameRowFactory.AdjustmentSummary(_game, shares, kind, _money);
            }

            var options = Options();
            var blocking = SettlementEngine.Validate(lines, options).Where(i => i.IsBlocking).ToList();
            Results.Clear();
            Payments.Clear();
            if (blocking.Count > 0)
            {
                ErrorText = Describe(blocking[0]);
                PaymentsTitle = "Payments";
                NoPaymentsText = null;
                // Results don't depend on who the banker is, so still show them.
                if (blocking.All(i => i.Kind == IssueKind.MissingBanker))
                {
                    var preview = SettlementEngine.Settle(lines, options with { Mode = SettlementMode.FewestPayments, BankerId = null });
                    foreach (var row in GameRowFactory.Results(_game, preview.Results, _money, showSettled: true)) Results.Add(row);
                }
                return;
            }

            var result = SettlementEngine.Settle(lines, options);
            foreach (var row in GameRowFactory.Results(_game, result.Results, _money, showSettled: true)) Results.Add(row);
            foreach (var p in result.Payments)
            {
                Payments.Add(new PaymentRow
                {
                    FromName = _game.FindPlayer(p.FromId)?.Name ?? "?",
                    ToName = _game.FindPlayer(p.ToId)?.Name ?? "?",
                    AmountText = Money.Format(p.Amount),
                });
            }
            PaymentsTitle = result.Payments.Count switch
            {
                0 => "Payments",
                1 => "1 payment",
                _ => $"{result.Payments.Count} payments",
            };
            NoPaymentsText = result.Payments.Count == 0 ? "Everyone is even. No payments needed." : null;
        }, "Recompute");
    }

    private string Describe(SettlementIssue issue) => issue.Kind switch
    {
        IssueKind.TooFewPlayers => "A game needs at least 2 players.",
        IssueKind.MissingCashOut or IssueKind.NegativeCashOut => "Some final amounts are missing. Go back and enter them.",
        IssueKind.Unbalanced => $"Cash-outs don't match buy-ins (off by {_money!.Units(Math.Abs(issue.Amount))}). Go back to fix them or choose how to handle the difference.",
        IssueKind.MissingBanker => "Choose the banker.",
        IssueKind.InvalidChipRate => "This game's chip rate is missing.",
        _ => "This game can't be finalized yet.",
    };

    [RelayCommand]
    private Task FinalizeAsync() => RunSafeAsync(async () =>
    {
        if (_game is null) return;
        Recompute();
        if (ErrorText is not null)
        {
            Log.ValidationFailed("review", ErrorText);
            return;
        }
        var options = Options();
        try
        {
            await Data.FinalizeGameAsync(_gameId, new FinalizeRequest(_resolution, _assignedId, options.Mode, options.BankerId, options.Rounding));
        }
        catch (SettlementValidationException ex)
        {
            ErrorText = Describe(ex.Issues[0]);
            return;
        }
        catch (GameNotEditableException)
        {
            Snackbar.Show("This game was already finalized.");
        }
        Messenger.Send(new DataChangedMessage(DataArea.Games | DataArea.Results | DataArea.Payments, _gameId));
        Snackbar.Show("Game finalized. Share the payments with everyone.");
        // Pop review, cash-out and live game (the stack is always live → cash-out → review), then show the result.
        await Navigation.GoToAsync(FinishedRoute, new Dictionary<string, object> { [Routes.GameId] = _gameId });
    });
}
