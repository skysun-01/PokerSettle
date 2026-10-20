using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PokerSettle.Core;
using PokerSettle.Core.Data;
using PokerSettle.Core.Diagnostics;
using PokerSettle.Core.Leaderboard;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Services;

namespace PokerSettle.Tests;

// Fake platform services, so ViewModels run against a real temp database without MAUI.

public sealed class FakeNavigation : INavigationService
{
    public List<(string Route, IDictionary<string, object>? Parameters)> Calls { get; } = [];

    public (string Route, IDictionary<string, object>? Parameters) Last => Calls[^1];

    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null)
    {
        Calls.Add((route, parameters));
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        Calls.Add(("..", null));
        return Task.CompletedTask;
    }
}

public sealed class FakeDialogs : IDialogService
{
    public bool ConfirmResult { get; set; } = true;
    public string? PromptResult { get; set; }
    public string? ChooseResult { get; set; }
    public List<string> Shown { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        Shown.Add(title);
        return Task.FromResult(ConfirmResult);
    }

    public Task AlertAsync(string title, string message, string ok = "OK")
    {
        Shown.Add(title);
        return Task.CompletedTask;
    }

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null)
    {
        Shown.Add(title);
        return Task.FromResult(PromptResult);
    }

    public Task<string?> ChooseAsync(string title, string cancel, IReadOnlyList<string> options)
    {
        Shown.Add(title);
        return Task.FromResult(ChooseResult);
    }
}

public sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; private set; }

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }
}

public sealed class FakeShare : IShareService
{
    public string? Text { get; private set; }
    public string? FilePath { get; private set; }

    public Task ShareTextAsync(string title, string text)
    {
        Text = text;
        return Task.CompletedTask;
    }

    public Task ShareFileAsync(string title, string filePath, string contentType)
    {
        FilePath = filePath;
        return Task.CompletedTask;
    }
}

public sealed class FakeFilePicker : IFilePickerService
{
    public PickedFile? Next { get; set; }

    public Task<PickedFile?> PickBackupFileAsync() => Task.FromResult(Next);
}

public sealed class FakeFileSaver : IFileSaverService
{
    public string? FileName { get; private set; }
    public byte[]? Content { get; private set; }

    public async Task<FileSaveOutcome> SaveAsync(string suggestedFileName, Stream content, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        FileName = suggestedFileName;
        Content = ms.ToArray();
        return new FileSaveOutcome(true, suggestedFileName, null);
    }
}

public sealed class FakeHaptics : IHapticsService
{
    public int Clicks { get; private set; }
    public void Click() => Clicks++;
}

public sealed class FakeScreen : IScreenService
{
    public bool KeepOn { get; private set; }
    public void SetKeepScreenOn(bool keepOn) => KeepOn = keepOn;
}

public sealed class FakeTheme : IThemeService
{
    public ThemeMode? Applied { get; private set; }
    public void Apply(ThemeMode mode) => Applied = mode;
}

public sealed class FakeLauncher : ILauncherService
{
    public List<string> Opened { get; } = [];
    public bool Result { get; set; }

    public Task<bool> TryOpenAsync(string uri)
    {
        Opened.Add(uri);
        return Task.FromResult(Result);
    }
}

public sealed class FakeEnvironment(string directory) : IAppEnvironment
{
    public string AppVersion => "1.0.0-test";
    public string BuildNumber => "1";
    public string Platform => "Test";
    public string DeviceDescription => "Test device";
    public string OsDescription => "Test OS";
    public string AppDataDirectory => directory;
    public string CacheDirectory => Path.Combine(directory, "cache");
    public bool IsDebugBuild => true;
    public bool IsAndroid => true;
}

public sealed class FakeLeaderboardTransport : ILeaderboardTransport
{
    public List<(string Url, string Json)> Posts { get; } = [];

    /// <summary>What the "Apps Script" answers; set to throw to simulate being offline.</summary>
    public Func<string, string> Respond { get; set; } = _ => """{"ok":true}""";

    public Task<string> PostAsync(string url, string json, CancellationToken cancellationToken)
    {
        Posts.Add((url, json));
        return Task.FromResult(Respond(json));
    }
}

/// <summary>Core services and ViewModels wired up with fakes and a temp database.</summary>
public sealed class Harness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public Harness(TempDir dir, FakeClock? clock = null, string databaseFile = "poker-settle.db")
    {
        Directory.CreateDirectory(Path.Combine(dir.Path, "cache"));
        Clock = clock ?? new FakeClock();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<IMessenger>(new WeakReferenceMessenger());
        services.AddSingleton<IPreferencesStore>(Preferences);
        services.AddSingleton(new LogSwitch(false));
        services.AddSingleton(new JsonLogWriter(new LogFileStore(Path.Combine(dir.Path, "logs"))));
        services.AddSingleton(new CrashMarker(dir.Path));
        services.AddSingleton(new DatabaseOptions { DirectoryPath = dir.Path, FileName = databaseFile });
        services.AddSingleton<INavigationService>(Navigation);
        services.AddSingleton<IDialogService>(Dialogs);
        services.AddSingleton<IClipboardService>(Clipboard);
        services.AddSingleton<IShareService>(Share);
        services.AddSingleton<IFilePickerService>(FilePicker);
        services.AddSingleton<IFileSaverService>(FileSaver);
        services.AddSingleton<IHapticsService>(Haptics);
        services.AddSingleton<IScreenService>(Screen);
        services.AddSingleton<IThemeService>(new FakeTheme());
        services.AddSingleton<ILauncherService>(Launcher);
        services.AddSingleton<IAppEnvironment>(new FakeEnvironment(dir.Path));
        services.AddPokerSettleCore();
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ILeaderboardTransport>(LeaderboardTransport);
        _provider = services.BuildServiceProvider();
    }

    public FakeClock Clock { get; }
    public FakePreferences Preferences { get; } = new();
    public FakeNavigation Navigation { get; } = new();
    public FakeDialogs Dialogs { get; } = new();
    public FakeClipboard Clipboard { get; } = new();
    public FakeShare Share { get; } = new();
    public FakeFilePicker FilePicker { get; } = new();
    public FakeFileSaver FileSaver { get; } = new();
    public FakeHaptics Haptics { get; } = new();
    public FakeScreen Screen { get; } = new();
    public FakeLauncher Launcher { get; } = new();
    public FakeLeaderboardTransport LeaderboardTransport { get; } = new();

    public DataStore Data => Get<DataStore>();
    public IMessenger Messenger => Get<IMessenger>();
    public SnackbarService Snackbar => Get<SnackbarService>();

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await Data.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
