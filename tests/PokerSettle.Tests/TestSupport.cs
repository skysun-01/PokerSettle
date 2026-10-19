using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PokerSettle.Core.Data;
using PokerSettle.Core.Services;

namespace PokerSettle.Tests;

/// <summary>A unique temp folder, deleted afterwards.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pokersettle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A handle may linger briefly on Windows; the OS temp cleaner gets it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => Now = now;

    public FakeClock() : this(new DateTimeOffset(2026, 10, 24, 21, 30, 0, TimeSpan.FromHours(5.5)))
    {
    }

    public DateTimeOffset Now { get; set; }

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public void Advance(TimeSpan by) => Now = Now.Add(by);
}

public static class Stores
{
    public static DataStore Create(TempDir dir, IClock? clock = null, ILoggerFactory? loggers = null, bool demo = false) =>
        new(new DatabaseOptions { DirectoryPath = dir.Path, StartWithDemo = demo },
            (loggers ?? NullLoggerFactory.Instance).CreateLogger<DataStore>(),
            clock ?? new FakeClock());

    public static async Task<DataStore> OpenAsync(TempDir dir, IClock? clock = null)
    {
        var store = Create(dir, clock);
        await store.InitializeAsync();
        return store;
    }

    public static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static object? Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}

/// <summary>In-memory Preferences.</summary>
public sealed class FakePreferences : IPreferencesStore
{
    private readonly Dictionary<string, object?> _values = [];

    public string? GetString(string key, string? defaultValue) => _values.TryGetValue(key, out var v) ? v as string : defaultValue;
    public void SetString(string key, string? value) => _values[key] = value;
    public bool GetBool(string key, bool defaultValue) => _values.TryGetValue(key, out var v) && v is bool b ? b : defaultValue;
    public void SetBool(string key, bool value) => _values[key] = value;
    public long GetLong(string key, long defaultValue) => _values.TryGetValue(key, out var v) && v is long l ? l : defaultValue;
    public void SetLong(string key, long value) => _values[key] = value;
    public void Remove(string key) => _values.Remove(key);
}
