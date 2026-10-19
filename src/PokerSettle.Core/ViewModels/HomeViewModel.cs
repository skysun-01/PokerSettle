using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Backup;
using PokerSettle.Core.Data;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Services;
using PokerSettle.Core.Stats;
using PokerSettle.Core.ViewModels.Board;
using PokerSettle.Core.ViewModels.Games;

namespace PokerSettle.Core.ViewModels;

public sealed partial class PendingItem : ObservableObject
{
    public required long TransferId { get; init; }
    public required long GameId { get; init; }
    public required string Text { get; init; }
    /// <summary>Within a payer's group: "To Rahul: ₹500".</summary>
    public required string ShortText { get; init; }
    public required string GameText { get; init; }
    public required IRelayCommand<PendingItem> MarkPaidCommand { get; init; }
    public required IRelayCommand<PendingItem> OpenGameCommand { get; init; }
    public string MarkPaidDescription => $"Mark paid: {Text}";
}

public sealed class PendingGroup
{
    public required string PayerName { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<PendingItem> Items { get; init; }
}

/// <summary>
/// Home: resume the game in progress, start a new one, top 3, last 5 games, pending payments grouped by
/// who owes, a gentle backup reminder, and the "closed unexpectedly" prompt after a crash.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly BackupCoordinator _backup;
    private readonly LogSwitch _logSwitch;
    private long? _inProgressId;

    public HomeViewModel(CoreServices services, BackupCoordinator backup, LogSwitch logSwitch) : base(services)
    {
        _backup = backup;
        _logSwitch = logSwitch;
        Messenger.Register<HomeViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (ReferenceEquals(m.Source, r)) return;
            r.IsStale = true;
            if (r.IsActive) _ = r.RunSafeAsync(r.LoadAsync, "Refresh");
        });
        Messenger.Register<HomeViewModel, SettingsChangedMessage>(this, static (r, _) => r.IsStale = true);
    }

    public string Title => "Poker Settle";

    public ObservableCollection<BoardItem> TopPlayers { get; } = [];
    public ObservableCollection<GameRow> RecentGames { get; } = [];
    public ObservableCollection<PendingGroup> PendingGroups { get; } = [];

    [ObservableProperty] public partial bool HasGameInProgress { get; set; }
    [ObservableProperty] public partial string ResumeTitle { get; set; } = "";
    [ObservableProperty] public partial string ResumeDetails { get; set; } = "";
    [ObservableProperty] public partial bool HasTopPlayers { get; set; }
    [ObservableProperty] public partial bool HasRecentGames { get; set; }
    [ObservableProperty] public partial bool HasPending { get; set; }
    [ObservableProperty] public partial string PendingTitle { get; set; } = "Pending payments";
    [ObservableProperty] public partial bool ShowWelcome { get; set; }
    [ObservableProperty] public partial string WelcomeText { get; set; } = "";
    [ObservableProperty] public partial bool ShowBackupReminder { get; set; }
    [ObservableProperty] public partial string BackupReminderText { get; set; } = "";
    [ObservableProperty] public partial bool ShowCrashPrompt { get; set; }

    protected override async Task OnAppearingCoreAsync()
    {
        await Data.InitializeAsync();
        if (Data.Recovery is not null && !Services.AppState.RecoveryShown)
        {
            Services.AppState.RecoveryShown = true;
            await Navigation.GoToAsync(Routes.Recovery);
            return;
        }
        ShowCrashPrompt = Services.AppState.ShowCrashPrompt;
        if (IsStale) await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var since = Max(Settings.LastBackupAt, Settings.BackupReminderDismissedAt);
        var home = await Data.GetHomeAsync(since);
        IsStale = false;

        _inProgressId = home.InProgress?.Id;
        HasGameInProgress = home.InProgress is not null;
        if (home.InProgress is { } g)
        {
            var money = new GameMoney(Money, new GameSettings { ChipMode = g.ChipMode, ChipRate = g.ChipRate });
            ResumeTitle = g.Title;
            ResumeDetails = $"{g.PlayerCount} players · pot {money.UnitsWithMoney(g.Pot)}";
        }

        TopPlayers.Clear();
        foreach (var row in BoardMath.RankByNet(home.Board).Take(3)) TopPlayers.Add(BoardItem.From(row, Money, OpenPlayerCommand));
        HasTopPlayers = TopPlayers.Count > 0;

        RecentGames.Clear();
        foreach (var game in home.RecentGames) RecentGames.Add(GameRow.From(game, Money, OpenGameCommand));
        HasRecentGames = RecentGames.Count > 0;

        PendingGroups.Clear();
        foreach (var group in home.PendingPayments.GroupBy(p => p.FromId))
        {
            var items = group.Select(p => new PendingItem
            {
                TransferId = p.TransferId,
                GameId = p.GameId,
                Text = $"{p.FromName} pays {p.ToName} {Money.Format(p.Amount)}",
                ShortText = $"To {p.ToName}: {Money.Format(p.Amount)}",
                GameText = p.GameTitle == Money.DefaultGameTitle(p.GameDate)
                    ? Money.FormatCompactDate(p.GameDate)
                    : $"{p.GameTitle} · {Money.FormatCompactDate(p.GameDate)}",
                MarkPaidCommand = MarkPaidCommand,
                OpenGameCommand = OpenPendingGameCommand,
            }).ToList();
            var total = group.Sum(p => p.Amount);
            PendingGroups.Add(new PendingGroup
            {
                PayerName = group.First().FromName,
                Summary = $"owes {Money.Format(total)}" + (items.Count > 1 ? $" in {items.Count} payments" : ""),
                Items = items,
            });
        }
        HasPending = PendingGroups.Count > 0;
        PendingTitle = home.PendingPayments.Count == 1 ? "1 pending payment" : $"{home.PendingPayments.Count} pending payments";

        ShowWelcome = home.FinalizedGames == 0 && home.InProgress is null;
        WelcomeText = home.PlayerCount == 0
            ? "Welcome! Start a game and add players as you go; they're remembered for next time."
            : "No finished games yet. Start one, and the board and history fill in.";

        ShowBackupReminder = home.GamesSinceBackup > 0;
        BackupReminderText = Settings.LastBackupAt is { } last
            ? $"{Plural(home.GamesSinceBackup, "game")} finished since your last backup ({Money.FormatCompactDate(DateOnly.FromDateTime(last.LocalDateTime))})."
            : home.GamesSinceBackup == 1
                ? "1 finished game is only on this device. A backup keeps it safe."
                : $"{home.GamesSinceBackup} finished games are only on this device. A backup keeps them safe.";
    }

    [RelayCommand]
    private Task ResumeAsync() => RunSafeAsync(() => _inProgressId is { } id
        ? Navigation.GoToAsync(Routes.LiveGame, new Dictionary<string, object> { [Routes.GameId] = id })
        : Task.CompletedTask);

    [RelayCommand]
    private Task OpenGameAsync(GameRow? row) => RunSafeAsync(() => row is null
        ? Task.CompletedTask
        : Navigation.GoToAsync(row.IsInProgress ? Routes.LiveGame : Routes.GameDetail, new Dictionary<string, object> { [Routes.GameId] = row.Id }));

    [RelayCommand]
    private Task OpenPendingGameAsync(PendingItem? item) => RunSafeAsync(() => item is null
        ? Task.CompletedTask
        : Navigation.GoToAsync(Routes.GameDetail, new Dictionary<string, object> { [Routes.GameId] = item.GameId }));

    [RelayCommand]
    private Task OpenPlayerAsync(BoardItem? item) => RunSafeAsync(() => item is null
        ? Task.CompletedTask
        : Navigation.GoToAsync(Routes.PlayerDetail, new Dictionary<string, object> { [Routes.PlayerId] = item.PlayerId }));

    [RelayCommand]
    private Task OpenBoardAsync() => RunSafeAsync(() => Navigation.GoToAsync(Routes.Tab(Routes.Board)));

    [RelayCommand]
    private Task OpenGamesAsync() => RunSafeAsync(() => Navigation.GoToAsync(Routes.Tab(Routes.Games)));

    [RelayCommand]
    private Task MarkPaidAsync(PendingItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;
        await Data.SetTransferPaidAsync(item.TransferId, true);
        Messenger.Send(new DataChangedMessage(DataArea.Payments, item.GameId, this));
        await LoadAsync();
        Snackbar.ShowUndo($"{item.Text}: paid", async () =>
        {
            await Data.SetTransferPaidAsync(item.TransferId, false);
            Messenger.Send(new DataChangedMessage(DataArea.Payments, item.GameId));
        });
    });

    [RelayCommand]
    private Task ExportBackupAsync() => RunSafeAsync(async () =>
    {
        if (await _backup.ExportToFileAsync()) await LoadAsync();
    });

    [RelayCommand]
    private Task DismissBackupReminderAsync() => RunSafeAsync(async () =>
    {
        Settings.BackupReminderDismissedAt = Clock.Now;
        ShowBackupReminder = false;
        await Task.CompletedTask;
    });

    [RelayCommand]
    private void EnableDebugLogging() => RunSafe(() =>
    {
        Settings.DebugLogging = true;
        _logSwitch.Enabled = true;
        Log.DebugLoggingEnabled();
        HandleCrashPrompt();
        Snackbar.Show("Debug logging is on. If it happens again, use Settings → Copy debug report.");
    });

    [RelayCommand]
    private Task ExportAfterCrashAsync() => RunSafeAsync(async () =>
    {
        if (await _backup.ExportToFileAsync()) HandleCrashPrompt();
    });

    [RelayCommand]
    private void DismissCrashPrompt() => HandleCrashPrompt();

    private void HandleCrashPrompt()
    {
        Services.AppState.CrashPromptHandled = true;
        ShowCrashPrompt = false;
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
