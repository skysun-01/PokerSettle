using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using PokerSettle.Core.Messaging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Money;
using PokerSettle.Core.Stats;

namespace PokerSettle.Core.Services;

/// <summary>
/// Typed app settings over Preferences. Kept out of the database so the debug-logging flag can be
/// read before the database opens. Changes to formatting settings rebuild <see cref="Formatter"/>.
/// </summary>
public sealed class AppSettings
{
    public const long FallbackBuyIn = 50_000; // ₹500

    private static class Keys
    {
        public const string Currency = "currency";
        public const string Culture = "culture";
        public const string DefaultBuyIn = "default_buy_in";
        public const string SettlementMode = "default_settlement_mode";
        public const string Rounding = "default_rounding";
        public const string Theme = "theme";
        public const string DebugLogging = "debug_logging";
        public const string KeepScreenOn = "keep_screen_on";
        public const string LastBackupAt = "last_backup_at";
        public const string BackupReminderDismissedAt = "backup_reminder_dismissed_at";
        public const string BoardSort = "board_sort";
        public const string BoardSortDescending = "board_sort_desc";
        public const string BoardFilter = "board_filter";
        public const string BoardFrom = "board_from";
        public const string BoardTo = "board_to";
        public const string BoardIncludeArchived = "board_include_archived";
        public const string PlayersShowArchived = "players_show_archived";
        public const string UseDemoDatabase = "use_demo_database";
    }

    private readonly IPreferencesStore _store;
    private readonly IMessenger _messenger;
    private MoneyFormatter? _formatter;

    public AppSettings(IPreferencesStore store, IMessenger messenger)
    {
        _store = store;
        _messenger = messenger;
    }

    /// <summary>Formatter for the configured culture and currency.</summary>
    public MoneyFormatter Formatter => _formatter ??= new MoneyFormatter(CultureName, CurrencySymbol);

    public string CurrencySymbol
    {
        get => _store.GetString(Keys.Currency, MoneyFormatter.DefaultCurrency) ?? MoneyFormatter.DefaultCurrency;
        set => SetFormatting(Keys.Currency, value);
    }

    public string CultureName
    {
        get => _store.GetString(Keys.Culture, MoneyFormatter.DefaultCulture) ?? MoneyFormatter.DefaultCulture;
        set => SetFormatting(Keys.Culture, value);
    }

    public long DefaultBuyIn
    {
        get => Math.Max(1, _store.GetLong(Keys.DefaultBuyIn, FallbackBuyIn));
        set => Set(Keys.DefaultBuyIn, value);
    }

    public SettlementMode DefaultSettlementMode
    {
        get => (SettlementMode)_store.GetLong(Keys.SettlementMode, 0);
        set => Set(Keys.SettlementMode, (long)value);
    }

    public RoundingMode DefaultRounding
    {
        get => (RoundingMode)_store.GetLong(Keys.Rounding, 0);
        set => Set(Keys.Rounding, (long)value);
    }

    public ThemeMode Theme
    {
        get => (ThemeMode)_store.GetLong(Keys.Theme, 0);
        set => Set(Keys.Theme, (long)value);
    }

    public bool DebugLogging
    {
        get => _store.GetBool(Keys.DebugLogging, false);
        set => Set(Keys.DebugLogging, value);
    }

    public bool KeepScreenOn
    {
        get => _store.GetBool(Keys.KeepScreenOn, true);
        set => Set(Keys.KeepScreenOn, value);
    }

    public DateTimeOffset? LastBackupAt
    {
        get => GetTimestamp(Keys.LastBackupAt);
        set => SetTimestamp(Keys.LastBackupAt, value);
    }

    public DateTimeOffset? BackupReminderDismissedAt
    {
        get => GetTimestamp(Keys.BackupReminderDismissedAt);
        set => SetTimestamp(Keys.BackupReminderDismissedAt, value);
    }

    public BoardColumn BoardSort
    {
        get => (BoardColumn)_store.GetLong(Keys.BoardSort, (long)BoardColumn.Net);
        set => _store.SetLong(Keys.BoardSort, (long)value);
    }

    public bool BoardSortDescending
    {
        get => _store.GetBool(Keys.BoardSortDescending, true);
        set => _store.SetBool(Keys.BoardSortDescending, value);
    }

    public TimeFilterKind BoardFilter
    {
        get => (TimeFilterKind)_store.GetLong(Keys.BoardFilter, (long)TimeFilterKind.AllTime);
        set => _store.SetLong(Keys.BoardFilter, (long)value);
    }

    public DateOnly? BoardFrom
    {
        get => GetDate(Keys.BoardFrom);
        set => SetDate(Keys.BoardFrom, value);
    }

    public DateOnly? BoardTo
    {
        get => GetDate(Keys.BoardTo);
        set => SetDate(Keys.BoardTo, value);
    }

    public bool BoardIncludeArchived
    {
        get => _store.GetBool(Keys.BoardIncludeArchived, false);
        set => _store.SetBool(Keys.BoardIncludeArchived, value);
    }

    public bool PlayersShowArchived
    {
        get => _store.GetBool(Keys.PlayersShowArchived, false);
        set => _store.SetBool(Keys.PlayersShowArchived, value);
    }

    /// <summary>Debug builds only: use the separate demo database file.</summary>
    public bool UseDemoDatabase
    {
        get => _store.GetBool(Keys.UseDemoDatabase, false);
        set => _store.SetBool(Keys.UseDemoDatabase, value);
    }

    /// <summary>Settings worth showing in the debug report (no personal data).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Describe() =>
    [
        new("currency", CurrencySymbol),
        new("culture", CultureName),
        new("defaultBuyIn", DefaultBuyIn.ToString(CultureInfo.InvariantCulture)),
        new("defaultSettlementMode", DefaultSettlementMode.ToString()),
        new("defaultRounding", DefaultRounding.ToString()),
        new("theme", Theme.ToString()),
        new("keepScreenOn", KeepScreenOn.ToString()),
        new("debugLogging", DebugLogging.ToString()),
        new("lastBackupAt", LastBackupAt?.ToString("O", CultureInfo.InvariantCulture) ?? "never"),
        new("useDemoDatabase", UseDemoDatabase.ToString()),
    ];

    private void SetFormatting(string key, string? value)
    {
        _store.SetString(key, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
        _formatter = null;
        _messenger.Send(new SettingsChangedMessage(key));
    }

    private void Set(string key, long value)
    {
        _store.SetLong(key, value);
        _messenger.Send(new SettingsChangedMessage(key));
    }

    private void Set(string key, bool value)
    {
        _store.SetBool(key, value);
        _messenger.Send(new SettingsChangedMessage(key));
    }

    private DateTimeOffset? GetTimestamp(string key)
    {
        var ms = _store.GetLong(key, 0);
        return ms == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    private void SetTimestamp(string key, DateTimeOffset? value)
    {
        if (value is null) _store.Remove(key);
        else _store.SetLong(key, value.Value.ToUnixTimeMilliseconds());
    }

    private DateOnly? GetDate(string key)
    {
        var text = _store.GetString(key, null);
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private void SetDate(string key, DateOnly? value) =>
        _store.SetString(key, value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

/// <summary>Choices offered in Settings.</summary>
public static class SettingsOptions
{
    public sealed record Option(string Value, string Label);

    public static readonly IReadOnlyList<Option> Currencies =
    [
        new("₹", "₹ Indian rupee"),
        new("$", "$ US dollar"),
        new("€", "€ Euro"),
        new("£", "£ British pound"),
        new("A$", "A$ Australian dollar"),
        new("C$", "C$ Canadian dollar"),
        new("S$", "S$ Singapore dollar"),
        new("AED", "AED UAE dirham"),
    ];

    public static readonly IReadOnlyList<Option> Cultures =
    [
        new("en-IN", "English (India) · 1,25,000.50"),
        new("en-US", "English (US) · 125,000.50"),
        new("en-GB", "English (UK) · 125,000.50"),
        new("en-AU", "English (Australia) · 125,000.50"),
        new("en-CA", "English (Canada) · 125,000.50"),
        new("en-SG", "English (Singapore) · 125,000.50"),
        new("de-DE", "Deutsch · 125.000,50"),
        new("fr-FR", "Français · 125 000,50"),
        new("es-ES", "Español · 125.000,50"),
        new("nl-NL", "Nederlands · 125.000,50"),
    ];
}
