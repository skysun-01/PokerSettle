using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.ViewModels.Settings;

public sealed partial class LogItem : ObservableObject
{
    public LogItem(LogRecord record)
    {
        Record = record;
        Time = record.Timestamp.ToLocalTime().ToString("dd MMM HH:mm:ss.fff", CultureInfo.InvariantCulture);
        Level = record.Level;
        Category = record.Category;
        Message = record.Message;
        DataText = record.Data is { Count: > 0 } data ? data.ToJsonString(LogJsonContext.Pretty.Options) : null;
        Severity = record.Level switch
        {
            "Critical" or "Error" => 2,
            "Warning" => 1,
            _ => 0,
        };
    }

    public LogRecord Record { get; }
    public string Time { get; }
    public string Level { get; }
    public string Category { get; }
    public string Message { get; }
    public string? DataText { get; }
    public bool HasData => DataText is not null;
    /// <summary>0 normal, 1 warning, 2 error.</summary>
    public int Severity { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded && HasData;
}

/// <summary>Logs screen: filter by level, search, expand an entry's data, copy, share and clear.</summary>
public sealed partial class LogsViewModel : ViewModelBase
{
    public static readonly IReadOnlyList<string> LevelOptions = ["All levels", "Information and above", "Warnings and errors", "Errors only"];

    private readonly JsonLogWriter _writer;
    private readonly IClipboardService _clipboard;
    private readonly IShareService _share;
    private readonly IAppEnvironment _environment;
    private List<LogItem> _all = [];

    public LogsViewModel(CoreServices services, JsonLogWriter writer, IClipboardService clipboard, IShareService share, IAppEnvironment environment)
        : base(services)
    {
        _writer = writer;
        _clipboard = clipboard;
        _share = share;
        _environment = environment;
    }

    public IReadOnlyList<string> Levels => LevelOptions;

    public ObservableCollection<LogItem> Entries { get; } = [];

    [ObservableProperty]
    public partial int LevelIndex { get; set; }

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    partial void OnLevelIndexChanged(int value) => ApplyFilter();

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    protected override Task OnAppearingCoreAsync() => LoadAsync();

    [RelayCommand]
    private Task RefreshAsync() => RunSafeAsync(LoadAsync);

    [RelayCommand]
    private Task CopyAllAsync() => RunSafeAsync(async () =>
    {
        var text = new StringBuilder();
        foreach (var item in Entries.Reverse()) text.AppendLine(JsonSerializer.Serialize(item.Record, LogJsonContext.Lines.LogRecord));
        await _clipboard.SetTextAsync(text.ToString());
        Snackbar.Show($"Copied {Entries.Count} entries");
    });

    [RelayCommand]
    private Task ShareFileAsync() => RunSafeAsync(async () =>
    {
        await _writer.FlushAsync();
        var target = Path.Combine(_environment.CacheDirectory, $"poker-settle-log-{Clock.Now:yyyy-MM-dd-HHmm}.jsonl");
        var path = await Task.Run(() => _writer.Store.ExportCombined(target));
        if (path is null)
        {
            Snackbar.Show("There are no log entries yet");
            return;
        }
        await _share.ShareFileAsync("Poker Settle log", path, "application/json");
    });

    [RelayCommand]
    private Task ClearAsync() => RunSafeAsync(async () =>
    {
        if (!await Dialogs.ConfirmAsync("Clear the log?", "All log entries will be deleted.", "Clear", "Cancel")) return;
        await Task.Run(_writer.Clear);
        await LoadAsync();
    });

    private async Task LoadAsync()
    {
        await _writer.FlushAsync();
        var records = await Task.Run(() => _writer.Store.ReadAll());
        _all = records.Select(r => new LogItem(r)).Reverse().ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var minimum = LevelIndex switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            _ => 0,
        };
        var search = SearchText?.Trim();
        var filtered = _all.Where(i => Rank(i.Level) >= minimum &&
            (string.IsNullOrEmpty(search) ||
             i.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             i.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             (i.DataText?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)));
        Entries.Clear();
        foreach (var item in filtered) Entries.Add(item);
        IsEmpty = Entries.Count == 0;
        Summary = _all.Count == 0
            ? "No entries yet"
            : $"{Entries.Count} of {_all.Count} entries · {FormatBytes(_writer.Store.TotalBytes())} on disk";
    }

    private static int Rank(string level) => level switch
    {
        "Critical" or "Error" => 3,
        "Warning" => 2,
        "Information" => 1,
        _ => 0,
    };

    internal static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Format(CultureInfo.InvariantCulture, "{0:0.#} KB", bytes / 1024.0),
        _ => string.Format(CultureInfo.InvariantCulture, "{0:0.##} MB", bytes / (1024.0 * 1024.0)),
    };
}
