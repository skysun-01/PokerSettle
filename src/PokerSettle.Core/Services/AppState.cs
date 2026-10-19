using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Diagnostics;
using PokerSettle.Core.Logging;

namespace PokerSettle.Core.Services;

/// <summary>Session-wide state shown across screens (DEMO banner, crash and recovery prompts).</summary>
public sealed partial class AppState : ObservableObject
{
    private readonly DataStore _data;

    public AppState(DataStore data, CrashMarker crashMarker)
    {
        _data = data;
        CrashMarker = crashMarker;
        IsDemo = data.IsDemo;
    }

    public CrashMarker CrashMarker { get; }

    [ObservableProperty]
    public partial bool IsDemo { get; set; }

    /// <summary>The "closed unexpectedly" prompt was answered or dismissed this session.</summary>
    public bool CrashPromptHandled { get; set; }

    public bool ShowCrashPrompt => CrashMarker.PreviousSessionCrashed && !CrashPromptHandled;

    public RecoveryInfo? Recovery => _data.Recovery;
}

/// <summary>Services every ViewModel needs, bundled to keep constructors short.</summary>
public sealed class CoreServices(
    DataStore data,
    AppSettings settings,
    INavigationService navigation,
    IDialogService dialogs,
    SnackbarService snackbar,
    IMessenger messenger,
    IClock clock,
    ILoggerFactory loggerFactory,
    AppState appState)
{
    public DataStore Data { get; } = data;
    public AppSettings Settings { get; } = settings;
    public INavigationService Navigation { get; } = navigation;
    public IDialogService Dialogs { get; } = dialogs;
    public SnackbarService Snackbar { get; } = snackbar;
    public IMessenger Messenger { get; } = messenger;
    public IClock Clock { get; } = clock;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public AppState AppState { get; } = appState;
}

/// <summary>
/// Keeps the screen awake while a game is in progress (if the setting is on). Re-evaluates when
/// games or the setting change.
/// </summary>
public sealed class KeepAwakeCoordinator
{
    private readonly DataStore _data;
    private readonly AppSettings _settings;
    private readonly IScreenService _screen;
    private readonly ILogger<KeepAwakeCoordinator> _log;

    public KeepAwakeCoordinator(DataStore data, AppSettings settings, IScreenService screen, IMessenger messenger, ILogger<KeepAwakeCoordinator> log)
    {
        _data = data;
        _settings = settings;
        _screen = screen;
        _log = log;
        messenger.Register<KeepAwakeCoordinator, Messaging.DataChangedMessage>(this, static (r, m) =>
        {
            if (m.Affects(Messaging.DataArea.Games)) _ = r.RefreshAsync();
        });
        messenger.Register<KeepAwakeCoordinator, Messaging.SettingsChangedMessage>(this, static (r, m) =>
        {
            if (m.Key == "keep_screen_on") _ = r.RefreshAsync();
        });
    }

    public bool IsKeepingAwake { get; private set; }

    public async Task RefreshAsync()
    {
        try
        {
            var inProgress = await _data.GetInProgressGameIdAsync() is not null;
            IsKeepingAwake = inProgress && _settings.KeepScreenOn;
            _screen.SetKeepScreenOn(IsKeepingAwake);
        }
        catch (Exception ex)
        {
            _log.PlatformServiceFailed(ex, "keep screen on");
        }
    }
}
