namespace PokerSettle.Core.Services;

// Small interfaces for platform features. PokerSettle.App implements them with MAUI;
// tests use fakes. Core never references MAUI.

public interface INavigationService
{
    /// <summary>Shell-style route, e.g. "livegame", "../review", "//home/gamedetail".</summary>
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);

    Task GoBackAsync();
}

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);

    Task AlertAsync(string title, string message, string ok = "OK");

    /// <summary>Text prompt; returns null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null);

    /// <summary>Action sheet; returns the chosen option or null.</summary>
    Task<string?> ChooseAsync(string title, string cancel, IReadOnlyList<string> options);
}

public interface IClipboardService
{
    Task SetTextAsync(string text);
}

public interface IShareService
{
    Task ShareTextAsync(string title, string text);

    Task ShareFileAsync(string title, string filePath, string contentType);
}

public sealed record PickedFile(string FileName, string Content);

public interface IFilePickerService
{
    /// <summary>Lets the user pick a backup file and reads it as text; null when cancelled.</summary>
    Task<PickedFile?> PickBackupFileAsync();
}

public sealed record FileSaveOutcome(bool Saved, string? Path, string? Error)
{
    public static FileSaveOutcome Cancelled { get; } = new(false, null, null);
}

public interface IFileSaverService
{
    /// <summary>Opens the system save dialog for the content.</summary>
    Task<FileSaveOutcome> SaveAsync(string suggestedFileName, Stream content, CancellationToken cancellationToken = default);
}

/// <summary>Key/value app settings (MAUI Preferences on device).</summary>
public interface IPreferencesStore
{
    string? GetString(string key, string? defaultValue);
    void SetString(string key, string? value);
    bool GetBool(string key, bool defaultValue);
    void SetBool(string key, bool value);
    long GetLong(string key, long defaultValue);
    void SetLong(string key, long value);
    void Remove(string key);
}

public interface IHapticsService
{
    /// <summary>A light tap; no-op where unsupported.</summary>
    void Click();
}

public interface IScreenService
{
    void SetKeepScreenOn(bool keepOn);
}

public interface IThemeService
{
    void Apply(Models.ThemeMode mode);
}

public interface ILauncherService
{
    /// <summary>Opens a URI with an external app; false if nothing can handle it.</summary>
    Task<bool> TryOpenAsync(string uri);
}

/// <summary>Facts about the app and device, for the debug report, startup log and file paths.</summary>
public interface IAppEnvironment
{
    string AppVersion { get; }
    string BuildNumber { get; }
    string Platform { get; }
    string DeviceDescription { get; }
    string OsDescription { get; }
    string AppDataDirectory { get; }
    string CacheDirectory { get; }
    bool IsDebugBuild { get; }
    bool IsAndroid { get; }
}

public interface IClock
{
    DateTimeOffset Now { get; }
    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
