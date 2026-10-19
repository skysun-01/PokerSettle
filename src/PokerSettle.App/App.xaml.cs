using Microsoft.Extensions.Logging;
using PokerSettle.Core.Data;
using PokerSettle.Core.Diagnostics;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Services;

namespace PokerSettle.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly CrashMarker _crashMarker;
    private readonly JsonLogWriter _logWriter;
    private readonly ILogger<App> _log;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _crashMarker = services.GetRequiredService<CrashMarker>();
        _logWriter = services.GetRequiredService<JsonLogWriter>();
        _log = services.GetRequiredService<ILogger<App>>();

        services.GetRequiredService<IThemeService>().Apply(services.GetRequiredService<AppSettings>().Theme);

        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashReporter.Report(e.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _log.UnobservedTaskException(e.Exception);
            e.SetObserved();
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_services.GetRequiredService<AppShell>()) { Title = "Poker Settle" };
        if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
        {
            window.Width = 1180;
            window.Height = 820;
            window.MinimumWidth = 420;
            window.MinimumHeight = 600;
        }

        _crashMarker.OnStartup();
        window.Resumed += (_, _) => OnForeground();
        window.Activated += (_, _) => _crashMarker.MarkActive();
        window.Stopped += (_, _) => OnBackground();
        window.Destroying += (_, _) => OnBackground();

        _ = StartAsync();
        return window;
    }

    /// <summary>Opens the database in the background; the first screen never waits for it.</summary>
    private async Task StartAsync()
    {
        try
        {
            var data = _services.GetRequiredService<DataStore>();
            await data.InitializeAsync();
            var environment = _services.GetRequiredService<IAppEnvironment>();
            _log.AppStarted(environment.AppVersion, data.SchemaVersion, environment.Platform, environment.DeviceDescription,
                environment.OsDescription, data.DatabaseSizeBytes(), data.IsDemo);
            if (_crashMarker.PreviousSessionCrashed) _log.PreviousSessionCrashed();
            await _services.GetRequiredService<KeepAwakeCoordinator>().RefreshAsync();
        }
        catch (Exception ex)
        {
            _log.UnhandledException(ex, "Startup");
        }
    }

    private void OnForeground()
    {
        _crashMarker.MarkActive();
        _log.AppResumed();
    }

    private void OnBackground()
    {
        _log.AppBackgrounded();
        _crashMarker.MarkInactive();
        _logWriter.Flush();
    }
}
