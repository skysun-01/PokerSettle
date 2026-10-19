using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace PokerSettle.Core.Logging;

/// <summary>The debug-logging flag. Read from Preferences at startup, before the database opens.</summary>
public sealed class LogSwitch(bool enabled)
{
    private volatile bool _enabled = enabled;

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }
}

/// <summary>
/// Microsoft.Extensions.Logging provider for the JSON-lines debug log. When the switch is off,
/// <see cref="ILogger.IsEnabled"/> returns false, so [LoggerMessage] methods return before doing any work.
/// </summary>
[ProviderAlias("JsonFile")]
public sealed class JsonFileLoggerProvider(LogSwitch logSwitch, JsonLogWriter writer, LogLevel minimumLevel = LogLevel.Debug)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, JsonFileLogger> _loggers = new(StringComparer.Ordinal);

    public LogSwitch Switch { get; } = logSwitch;
    public JsonLogWriter Writer { get; } = writer;
    public LogLevel MinimumLevel { get; } = minimumLevel;

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, static (name, provider) => new JsonFileLogger(name, provider), this);

    public void Dispose() => Writer.Flush();
}

internal sealed partial class JsonFileLogger(string category, JsonFileLoggerProvider provider) : ILogger
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly string _shortCategory = category.StartsWith("PokerSettle.", StringComparison.Ordinal)
        ? category[(category.LastIndexOf('.') + 1)..]
        : category;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && provider.Switch.Enabled && logLevel >= provider.MinimumLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        JsonObject? data = null;
        if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            string? template = null;
            var sensitive = false;
            foreach (var (key, value) in values)
            {
                if (key == OriginalFormatKey)
                {
                    template = value as string;
                    continue;
                }
                sensitive |= IsSensitiveKey(key);
                data ??= [];
                data[key] = ToNode(key, value);
            }
            if (sensitive && template is not null) message = RenderRedacted(template, values);
        }
        if (exception is not null)
        {
            data ??= [];
            data["exceptionType"] = exception.GetType().FullName;
            data["exception"] = exception.ToString();
        }
        if (eventId.Id != 0)
        {
            data ??= [];
            data["eventId"] = eventId.Id;
        }

        provider.Writer.Enqueue(new LogRecord
        {
            Timestamp = DateTimeOffset.Now,
            Level = logLevel.ToString(),
            Category = _shortCategory,
            Message = message,
            Data = data,
        });
    }

    /// <summary>Phone numbers and UPI IDs never reach the log, even if someone passes them by mistake.</summary>
    internal static bool IsSensitiveKey(string key) =>
        key.Contains("phone", StringComparison.OrdinalIgnoreCase) || key.Contains("upi", StringComparison.OrdinalIgnoreCase);

    /// <summary>Re-renders a message template with sensitive placeholders replaced by "[redacted]".</summary>
    private static string RenderRedacted(string template, IReadOnlyList<KeyValuePair<string, object?>> values) =>
        Placeholder().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (IsSensitiveKey(key)) return "[redacted]";
            foreach (var (k, v) in values)
            {
                if (k == key) return v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v?.ToString() ?? "(null)";
            }
            return match.Value;
        });

    [System.Text.RegularExpressions.GeneratedRegex(@"\{@?([^{}:,]+)(?:[,:][^{}]*)?\}")]
    private static partial System.Text.RegularExpressions.Regex Placeholder();

    private static JsonNode? ToNode(string key, object? value)
    {
        if (IsSensitiveKey(key)) return "[redacted]";
        return value switch
        {
            null => null,
            string s => s,
            bool b => b,
            int i => i,
            long l => l,
            double d => d,
            decimal m => m,
            float f => f,
            short s16 => s16,
            byte u8 => u8,
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeSpan ts => ts.TotalMilliseconds,
            Enum e => e.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}
