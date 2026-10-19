using CommunityToolkit.Maui.Storage;
using Microsoft.Extensions.Logging;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Services;

namespace PokerSettle.App.Services;

// MAUI implementations of the small Core platform interfaces.

public sealed class MauiNavigationService(ILogger<MauiNavigationService> log) : INavigationService
{
    public Task GoToAsync(string route, IDictionary<string, object>? parameters = null) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            log.Navigated(route);
            if (Shell.Current is null) return;
            if (parameters is null) await Shell.Current.GoToAsync(route);
            else await Shell.Current.GoToAsync(route, parameters);
        });

    public Task GoBackAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (Shell.Current is null) return;
        if (Shell.Current.Navigation.NavigationStack.Count > 1) await Shell.Current.GoToAsync("..");
    });
}

public sealed class MauiDialogService : IDialogService
{
    private static Page? CurrentPage => Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        MainThread.InvokeOnMainThreadAsync(() => CurrentPage?.DisplayAlertAsync(title, message, accept, cancel) ?? Task.FromResult(false));

    public Task AlertAsync(string title, string message, string ok = "OK") =>
        MainThread.InvokeOnMainThreadAsync(() => CurrentPage?.DisplayAlertAsync(title, message, ok) ?? Task.CompletedTask);

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null) =>
        MainThread.InvokeOnMainThreadAsync(async () => CurrentPage is { } page
            ? await page.DisplayPromptAsync(title, message, accept, cancel, placeholder)
            : null);

    public Task<string?> ChooseAsync(string title, string cancel, IReadOnlyList<string> options) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (CurrentPage is not { } page) return null;
            var choice = await page.DisplayActionSheetAsync(title, cancel, null, options.ToArray());
            return choice == cancel ? null : choice;
        });
}

public sealed class MauiClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) => MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));
}

public sealed class MauiShareService : IShareService
{
    public Task ShareTextAsync(string title, string text) =>
        MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareTextRequest { Title = title, Text = text }));

    public Task ShareFileAsync(string title, string filePath, string contentType) =>
        MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(filePath, contentType),
        }));
}

public sealed class MauiFilePickerService : IFilePickerService
{
    // Many Android file managers label .json files as octet-stream or text, so accept anything there
    // and let import validation reject files that aren't backups.
    private static readonly FilePickerFileType BackupTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["*/*"],
        [DevicePlatform.WinUI] = [".json"],
        [DevicePlatform.iOS] = ["public.json", "public.text"],
        [DevicePlatform.MacCatalyst] = ["public.json", "public.text"],
    });

    public async Task<PickedFile?> PickBackupFileAsync()
    {
        var result = await MainThread.InvokeOnMainThreadAsync(() =>
            FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a Poker Settle backup", FileTypes = BackupTypes }));
        if (result is null) return null;
        await using var stream = await result.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();
        return new PickedFile(result.FileName, content);
    }
}

public sealed class MauiFileSaverService : IFileSaverService
{
    public async Task<FileSaveOutcome> SaveAsync(string suggestedFileName, Stream content, CancellationToken cancellationToken = default)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(() => FileSaver.Default.SaveAsync(suggestedFileName, content, cancellationToken));
        if (result.IsSuccessful) return new FileSaveOutcome(true, result.FilePath, null);
        // Cancelling the dialog is reported as a failure with an exception; treat it as a cancel.
        return result.Exception is null or OperationCanceledException || result.Exception.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase)
            ? FileSaveOutcome.Cancelled
            : new FileSaveOutcome(false, null, result.Exception.Message);
    }
}

public sealed class MauiPreferencesStore : IPreferencesStore
{
    private static IPreferences Store => Preferences.Default;

    public string? GetString(string key, string? defaultValue) => Store.Get(key, defaultValue);
    public void SetString(string key, string? value)
    {
        if (value is null) Store.Remove(key);
        else Store.Set(key, value);
    }
    public bool GetBool(string key, bool defaultValue) => Store.Get(key, defaultValue);
    public void SetBool(string key, bool value) => Store.Set(key, value);
    public long GetLong(string key, long defaultValue) => Store.Get(key, defaultValue);
    public void SetLong(string key, long value) => Store.Set(key, value);
    public void Remove(string key) => Store.Remove(key);
}

public sealed class MauiHapticsService(ILogger<MauiHapticsService> log) : IHapticsService
{
    public void Click()
    {
        if (DeviceInfo.Platform != DevicePlatform.Android) return;
        try
        {
            if (HapticFeedback.Default.IsSupported) HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch (Exception ex)
        {
            log.PlatformServiceFailed(ex, "haptics");
        }
    }
}

public sealed class MauiScreenService(ILogger<MauiScreenService> log) : IScreenService
{
    public void SetKeepScreenOn(bool keepOn) => MainThread.BeginInvokeOnMainThread(() =>
    {
        try
        {
            DeviceDisplay.Current.KeepScreenOn = keepOn;
        }
        catch (Exception ex)
        {
            log.PlatformServiceFailed(ex, "keep screen on");
        }
    });
}

public sealed class MauiThemeService : IThemeService
{
    public void Apply(ThemeMode mode) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (Application.Current is null) return;
        Application.Current.UserAppTheme = mode switch
        {
            ThemeMode.Light => AppTheme.Light,
            ThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    });
}

public sealed class MauiLauncherService : ILauncherService
{
    public Task<bool> TryOpenAsync(string uri) => MainThread.InvokeOnMainThreadAsync(() => Launcher.Default.TryOpenAsync(uri));
}

public sealed class MauiAppEnvironment : IAppEnvironment
{
    public string AppVersion => AppInfo.Current.VersionString;
    public string BuildNumber => AppInfo.Current.BuildString;
    public string Platform => DeviceInfo.Current.Platform.ToString();
    public string DeviceDescription => $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model} ({DeviceInfo.Current.Idiom}, {DeviceInfo.Current.DeviceType})";
    public string OsDescription => $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}";
    public string AppDataDirectory => FileSystem.AppDataDirectory;
    public string CacheDirectory => FileSystem.CacheDirectory;
    public bool IsAndroid => DeviceInfo.Current.Platform == DevicePlatform.Android;
#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif
}
