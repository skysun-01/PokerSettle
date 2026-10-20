using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Diagnostics;
using PokerSettle.Core.Leaderboard;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels.Settings;

/// <summary>Settings: money formatting, game defaults, theme, keep-awake, data tools, debug logging, about.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> SettlementLabels = ["Fewest payments", "Banker"];
    public static readonly IReadOnlyList<string> RoundingLabels = ["No rounding", "Nearest 1", "Nearest 10"];
    public static readonly IReadOnlyList<string> ThemeLabels = ["Follow system", "Light", "Dark"];

    private readonly LogSwitch _logSwitch;
    private readonly JsonLogWriter _logWriter;
    private readonly IThemeService _theme;
    private readonly IClipboardService _clipboard;
    private readonly IAppEnvironment _environment;
    private readonly DebugReportBuilder _report;
    private readonly Backup.BackupCoordinator _backup;
    private readonly LeaderboardPublisher _leaderboard;
    private readonly ILauncherService _launcher;
    private bool _loading;

    public SettingsViewModel(
        CoreServices services,
        LogSwitch logSwitch,
        JsonLogWriter logWriter,
        IThemeService theme,
        IClipboardService clipboard,
        IAppEnvironment environment,
        DebugReportBuilder report,
        Backup.BackupCoordinator backup,
        LeaderboardPublisher leaderboard,
        ILauncherService launcher)
        : base(services)
    {
        _logSwitch = logSwitch;
        _logWriter = logWriter;
        _theme = theme;
        _clipboard = clipboard;
        _environment = environment;
        _report = report;
        _backup = backup;
        _leaderboard = leaderboard;
        _launcher = launcher;
        IsDeveloperVisible = environment.IsDebugBuild;
        LoadFromSettings();
        Messenger.Register<SettingsViewModel, DataChangedMessage>(this, static (r, m) =>
        {
            if (r.IsActive) _ = r.RunSafeAsync(r.RefreshInfoAsync, "Refresh");
        });
    }

    [RelayCommand]
    private Task ExportAsync() => RunSafeAsync(async () =>
    {
        await _backup.ExportToFileAsync();
        await RefreshInfoAsync();
    });

    [RelayCommand]
    private Task ShareBackupAsync() => RunSafeAsync(async () =>
    {
        await _backup.ShareAsync();
        await RefreshInfoAsync();
    });

    [RelayCommand]
    private Task ImportAsync() => RunSafeAsync(async () =>
    {
        if (await _backup.ImportAsync())
        {
            LoadFromSettings();
            await RefreshInfoAsync();
        }
    });

    [RelayCommand]
    private Task ClearAllAsync() => RunSafeAsync(async () =>
    {
        if (await _backup.ClearAllAsync()) await RefreshInfoAsync();
    });

    public IReadOnlyList<string> CurrencyLabels { get; } = SettingsOptions.Currencies.Select(c => c.Label).ToList();
    public IReadOnlyList<string> CultureLabels { get; } = SettingsOptions.Cultures.Select(c => c.Label).ToList();
    public IReadOnlyList<string> SettlementOptionsList => SettlementLabels;
    public IReadOnlyList<string> RoundingOptions => RoundingLabels;
    public IReadOnlyList<string> ThemeOptions => ThemeLabels;

    [ObservableProperty]
    public partial int CurrencyIndex { get; set; }

    [ObservableProperty]
    public partial int CultureIndex { get; set; }

    [ObservableProperty]
    public partial string FormatPreview { get; set; } = "";

    [ObservableProperty]
    public partial string DefaultBuyInText { get; set; } = "";

    [ObservableProperty]
    public partial string? DefaultBuyInError { get; set; }

    [ObservableProperty]
    public partial int SettlementIndex { get; set; }

    [ObservableProperty]
    public partial int RoundingIndex { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool KeepScreenOn { get; set; }

    [ObservableProperty]
    public partial bool DebugLogging { get; set; }

    [ObservableProperty]
    public partial bool UseDemoDatabase { get; set; }

    public bool IsDeveloperVisible { get; }

    [ObservableProperty]
    public partial string LeaderboardUrl { get; set; } = "";

    [ObservableProperty]
    public partial string LeaderboardSecret { get; set; } = "";

    [ObservableProperty]
    public partial bool LeaderboardAutoPublish { get; set; }

    [ObservableProperty]
    public partial string LeaderboardStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishLeaderboardCommand))]
    public partial bool IsPublishing { get; set; }

    [ObservableProperty]
    public partial string AppVersionText { get; set; } = "";

    [ObservableProperty]
    public partial string SchemaVersionText { get; set; } = "";

    [ObservableProperty]
    public partial string DatabasePath { get; set; } = "";

    [ObservableProperty]
    public partial string DatabaseSizeText { get; set; } = "";

    [ObservableProperty]
    public partial string DataSummary { get; set; } = "";

    [ObservableProperty]
    public partial string BackupSummary { get; set; } = "";

    protected override async Task OnAppearingCoreAsync()
    {
        LoadFromSettings();
        await RefreshInfoAsync();
    }

    private void LoadFromSettings()
    {
        _loading = true;
        try
        {
            CurrencyIndex = IndexOf(SettingsOptions.Currencies, Settings.CurrencySymbol);
            CultureIndex = IndexOf(SettingsOptions.Cultures, Settings.CultureName);
            DefaultBuyInText = Money.FormatForInput(Settings.DefaultBuyIn);
            DefaultBuyInError = null;
            SettlementIndex = (int)Settings.DefaultSettlementMode;
            RoundingIndex = (int)Settings.DefaultRounding;
            ThemeIndex = (int)Settings.Theme;
            KeepScreenOn = Settings.KeepScreenOn;
            DebugLogging = Settings.DebugLogging;
            UseDemoDatabase = Data.IsDemo;
            LeaderboardUrl = Settings.LeaderboardUrl ?? "";
            LeaderboardSecret = Settings.LeaderboardSecret ?? "";
            LeaderboardAutoPublish = Settings.LeaderboardAutoPublish;
            UpdateLeaderboardStatus();
            UpdatePreview();
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task RefreshInfoAsync()
    {
        AppVersionText = $"{_environment.AppVersion} (build {_environment.BuildNumber}){(_environment.IsDebugBuild ? " · Debug" : "")}";
        var info = await Data.GetInfoAsync();
        SchemaVersionText = info.SchemaVersion.ToString(CultureInfo.InvariantCulture);
        DatabasePath = info.Path;
        DatabaseSizeText = LogsViewModel.FormatBytes(info.SizeBytes);
        DataSummary = $"{info.Players} players ({info.ArchivedPlayers} archived) · {info.Games} games · {info.Snapshots} safety snapshots";
        BackupSummary = Settings.LastBackupAt is { } last
            ? $"Last backup {Money.FormatDateTime(last)}"
            : "No backup yet";
    }

    partial void OnCurrencyIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= SettingsOptions.Currencies.Count) return;
        Settings.CurrencySymbol = SettingsOptions.Currencies[value].Value;
        Log.SettingChanged("currency", Settings.CurrencySymbol);
        AfterFormattingChanged();
    }

    partial void OnCultureIndexChanged(int value)
    {
        if (_loading || value < 0 || value >= SettingsOptions.Cultures.Count) return;
        Settings.CultureName = SettingsOptions.Cultures[value].Value;
        Log.SettingChanged("culture", Settings.CultureName);
        AfterFormattingChanged();
    }

    partial void OnSettlementIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        Settings.DefaultSettlementMode = (SettlementMode)value;
        Log.SettingChanged("defaultSettlementMode", Settings.DefaultSettlementMode.ToString());
    }

    partial void OnRoundingIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        Settings.DefaultRounding = (RoundingMode)value;
        Log.SettingChanged("defaultRounding", Settings.DefaultRounding.ToString());
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading || value < 0) return;
        Settings.Theme = (ThemeMode)value;
        _theme.Apply(Settings.Theme);
        Log.SettingChanged("theme", Settings.Theme.ToString());
    }

    partial void OnKeepScreenOnChanged(bool value)
    {
        if (_loading) return;
        Settings.KeepScreenOn = value;
        Log.SettingChanged("keepScreenOn", value.ToString());
    }

    partial void OnDebugLoggingChanged(bool value)
    {
        if (_loading) return;
        Settings.DebugLogging = value;
        if (value)
        {
            _logSwitch.Enabled = true;
            Log.DebugLoggingEnabled();
            Log.AppStarted(_environment.AppVersion, Data.SchemaVersion, _environment.Platform, _environment.DeviceDescription,
                _environment.OsDescription, Data.DatabaseSizeBytes(), Data.IsDemo);
        }
        else
        {
            _logWriter.Flush();
            _logSwitch.Enabled = false;
        }
    }

    partial void OnLeaderboardAutoPublishChanged(bool value)
    {
        if (_loading) return;
        Settings.LeaderboardAutoPublish = value;
        Log.SettingChanged("leaderboardAutoPublish", value.ToString());
    }

    /// <summary>Saves the web app URL and secret when a field is completed or loses focus.</summary>
    [RelayCommand]
    private void SaveLeaderboard() => RunSafe(() =>
    {
        var url = LeaderboardUrl.Trim();
        var secret = LeaderboardSecret.Trim();
        if (url == (Settings.LeaderboardUrl ?? "") && secret == (Settings.LeaderboardSecret ?? "")) return;
        Settings.LeaderboardUrl = url;
        Settings.LeaderboardSecret = secret;
        // Never log the URL or secret: either one lets someone write to the sheet.
        Log.SettingChanged("leaderboard", _leaderboard.IsConfigured ? "configured" : "not configured");
        UpdateLeaderboardStatus();
    });

    private bool CanPublishLeaderboard() => !IsPublishing;

    [RelayCommand(CanExecute = nameof(CanPublishLeaderboard))]
    private Task PublishLeaderboardAsync() => RunSafeAsync(async () =>
    {
        SaveLeaderboard();
        IsPublishing = true;
        try
        {
            LeaderboardStatus = "Publishing…";
            var result = await _leaderboard.PublishAsync();
            UpdateLeaderboardStatus();
            switch (result.Status)
            {
                case LeaderboardPublishStatus.Published:
                    Snackbar.Show(result.Players == 1 ? "Leaderboard published (1 player)" : $"Leaderboard published ({result.Players} players)");
                    break;
                case LeaderboardPublishStatus.NotConfigured:
                    LeaderboardStatus = "Enter the web app URL (https://…/exec) and the secret first.";
                    break;
                case LeaderboardPublishStatus.DemoDatabase:
                    LeaderboardStatus = "Not published: the demo database is in use.";
                    break;
                default:
                    LeaderboardStatus = result.Error ?? "Couldn't publish the leaderboard.";
                    break;
            }
        }
        finally
        {
            IsPublishing = false;
        }
    });

    /// <summary>Opens the leaderboard web page (the same /exec URL, which everyone can view).</summary>
    [RelayCommand]
    private Task OpenLeaderboardAsync() => RunSafeAsync(async () =>
    {
        SaveLeaderboard();
        if (!_leaderboard.IsConfigured)
        {
            LeaderboardStatus = "Enter the web app URL (https://…/exec) and the secret first.";
            return;
        }
        if (!await _launcher.TryOpenAsync(Settings.LeaderboardUrl!)) Snackbar.Show("Couldn't open a browser.");
    });

    /// <summary>Copies the view link to share with the group (the URL only, never the secret).</summary>
    [RelayCommand]
    private Task CopyLeaderboardLinkAsync() => RunSafeAsync(async () =>
    {
        SaveLeaderboard();
        if (string.IsNullOrEmpty(Settings.LeaderboardUrl)) return;
        await _clipboard.SetTextAsync(Settings.LeaderboardUrl);
        Snackbar.Show("Leaderboard link copied. Share it with the group.");
    });

    private void UpdateLeaderboardStatus() =>
        LeaderboardStatus = !_leaderboard.IsConfigured
            ? "Not set up. Follow the steps in the README to create the Google Sheet and web app."
            : Settings.LeaderboardLastPublishedAt is { } last
                ? $"Last published {Money.FormatDateTime(last)}"
                : "Set up, not published yet.";

    partial void OnUseDemoDatabaseChanged(bool value)
    {
        if (_loading || !IsDeveloperVisible) return;
        _ = RunSafeAsync(async () =>
        {
            Settings.UseDemoDatabase = value;
            await Data.SwitchDatabaseAsync(value);
            if (value && (await Data.GetInfoAsync()).Games == 0)
            {
                Snackbar.Show("Creating demo data (40 players, 2,000 games)…");
                await Data.SeedDemoDataAsync();
            }
            Services.AppState.IsDemo = Data.IsDemo;
            Messenger.Send(new DataChangedMessage(DataArea.All));
            await RefreshInfoAsync();
            Snackbar.Show(value ? "Using the demo database" : "Back to your real data");
        }, "UseDemoDatabase");
    }

    /// <summary>Saves the default buy-in when the field is completed or loses focus.</summary>
    [RelayCommand]
    private void SaveDefaultBuyIn() => RunSafe(() =>
    {
        var parsed = MoneyParser.ParseMoney(DefaultBuyInText, Money, allowZero: false);
        if (!parsed.Success)
        {
            DefaultBuyInError = parsed.Error;
            return;
        }
        DefaultBuyInError = null;
        if (parsed.Value != Settings.DefaultBuyIn)
        {
            Settings.DefaultBuyIn = parsed.Value;
            Log.SettingChanged("defaultBuyIn", parsed.Value.ToString(CultureInfo.InvariantCulture));
        }
        DefaultBuyInText = Money.FormatForInput(parsed.Value);
    });

    [RelayCommand]
    private Task OpenLogsAsync() => RunSafeAsync(() => Navigation.GoToAsync(Routes.Logs));

    [RelayCommand]
    private Task CopyDebugReportAsync() => RunSafeAsync(async () =>
    {
        var report = await _report.BuildAsync();
        await _clipboard.SetTextAsync(report);
        Snackbar.Show("Debug report copied. Paste it wherever you need it.");
    });

    private void AfterFormattingChanged()
    {
        _loading = true;
        try
        {
            DefaultBuyInText = Money.FormatForInput(Settings.DefaultBuyIn);
        }
        finally
        {
            _loading = false;
        }
        UpdatePreview();
        Messenger.Send(new DataChangedMessage(DataArea.All));
    }

    private void UpdatePreview() =>
        FormatPreview = $"{Money.FormatSigned(12_500_050)}  ·  {Money.FormatSigned(-50_000)}  ·  {Money.FormatSigned(0)}";

    private static int IndexOf(IReadOnlyList<SettingsOptions.Option> options, string value)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Value, value, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return 0;
    }
}
