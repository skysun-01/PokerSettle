using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Services;

namespace PokerSettle.Core.Data;

/// <summary>
/// The only class that touches the database. One long-lived connection (WAL, foreign keys on,
/// synchronous=FULL); every call is async, serialized, and runs on a background thread.
/// Multi-row changes run in a single transaction.
/// </summary>
public sealed partial class DataStore : IAsyncDisposable
{
    private readonly ILogger<DataStore> _log;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _initLock = new();
    private Task? _initTask;
    private SqliteConnection? _connection;
    private string _fileName;

    public DataStore(DatabaseOptions options, ILogger<DataStore> log, IClock clock)
    {
        Options = options;
        _log = log;
        _clock = clock;
        _fileName = options.StartWithDemo ? options.DemoFileName : options.FileName;
    }

    public DatabaseOptions Options { get; }

    public string DatabasePath => Path.Combine(Options.DirectoryPath, _fileName);

    public bool IsDemo => _fileName == Options.DemoFileName;

    public int SchemaVersion => Migrations.Latest;

    /// <summary>Set when the database couldn't be used at startup and a fresh one was created.</summary>
    public RecoveryInfo? Recovery { get; private set; }

    public void ClearRecovery() => Recovery = null;

    /// <summary>Opens, checks and migrates the database on a background thread. Safe to call many times.</summary>
    public Task InitializeAsync()
    {
        lock (_initLock)
        {
            return _initTask ??= Task.Run(() => _connection = OpenWithRecovery(DatabasePath));
        }
    }

    public long DatabaseSizeBytes()
    {
        long size = 0;
        foreach (var suffix in new[] { "", "-wal" })
        {
            var file = new FileInfo(DatabasePath + suffix);
            if (file.Exists) size += file.Length;
        }
        return size;
    }

    /// <summary>Debug builds: switch between the real and the demo database files.</summary>
    public async Task SwitchDatabaseAsync(bool demo)
    {
        await InitializeAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                _connection?.Dispose();
                _connection = null;
                _fileName = demo ? Options.DemoFileName : Options.FileName;
                _connection = OpenWithRecovery(DatabasePath);
                _log.DatabaseSwitched(_fileName);
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_initTask is not null)
        {
            try
            {
                await _initTask.ConfigureAwait(false);
            }
            catch
            {
                // Nothing to close.
            }
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs <paramref name="work"/> on a background thread with exclusive use of the connection.</summary>
    internal async Task<T> RunAsync<T>(Func<Sql, T> work, bool transaction, [CallerMemberName] string operation = "")
    {
        await InitializeAsync().ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var start = Stopwatch.GetTimestamp();
                var connection = _connection ?? throw new InvalidOperationException("The database is not open.");
                try
                {
                    if (!transaction) return work(new Sql(connection, null));
                    using var tx = connection.BeginTransaction();
                    var result = work(new Sql(connection, tx));
                    tx.Commit();
                    return result;
                }
                catch (Exception ex) when (ex is not (DuplicatePlayerNameException or GameInProgressException or GameNotEditableException or SettlementValidationException))
                {
                    _log.DatabaseOperationFailed(ex, operation);
                    throw;
                }
                finally
                {
                    _log.DatabaseOperation(operation, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal Task<T> ReadAsync<T>(Func<Sql, T> work, [CallerMemberName] string operation = "") =>
        RunAsync(work, transaction: false, operation);

    internal Task<T> WriteAsync<T>(Func<Sql, T> work, [CallerMemberName] string operation = "") =>
        RunAsync(work, transaction: true, operation);

    internal Task WriteAsync(Action<Sql> work, [CallerMemberName] string operation = "") =>
        RunAsync(sql =>
        {
            work(sql);
            return true;
        }, transaction: true, operation);

    /// <summary>
    /// Saves a consistent copy (VACUUM INTO) in the snapshots folder and keeps the newest few.
    /// Called before import, restore and clear.
    /// </summary>
    public Task<string> CreateSnapshotAsync(string reason) => ReadAsync(sql => CreateSnapshot(sql, reason));

    public IReadOnlyList<FileInfo> ListSnapshots()
    {
        var dir = new DirectoryInfo(Options.SnapshotsDirectory);
        if (!dir.Exists) return [];
        return dir.GetFiles(SnapshotPrefix + "*.db").OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList();
    }

    private string SnapshotPrefix => Path.GetFileNameWithoutExtension(_fileName) + "-";

    private string CreateSnapshot(Sql sql, string reason)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(Options.SnapshotsDirectory);
        var stamp = _clock.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(Options.SnapshotsDirectory, $"{SnapshotPrefix}{stamp}-{reason}.db");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(Options.SnapshotsDirectory, $"{SnapshotPrefix}{stamp}-{reason}-{n}.db");

        sql.Execute("VACUUM INTO $path;", ("$path", path));

        foreach (var old in ListSnapshots().Skip(Options.SnapshotsToKeep))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
                // Try again next time.
            }
        }
        _log.SnapshotCreated(Path.GetFileName(path), new FileInfo(path).Length, stopwatch.ElapsedMilliseconds);
        return path;
    }

    public Task<DatabaseInfo> GetInfoAsync() => ReadAsync(sql =>
    {
        int Count(string text) => (int)sql.ScalarLong(text);
        return new DatabaseInfo(
            DatabasePath,
            DatabaseSizeBytes(),
            Migrations.GetVersion(sql.Connection),
            Count("SELECT COUNT(*) FROM players WHERE is_archived = 0"),
            Count("SELECT COUNT(*) FROM players WHERE is_archived = 1"),
            Count("SELECT COUNT(*) FROM games"),
            Count("SELECT COUNT(*) FROM games WHERE status = 0"),
            Count("SELECT COUNT(*) FROM entries"),
            Count("SELECT COUNT(*) FROM buy_ins"),
            Count("SELECT COUNT(*) FROM transfers"),
            Count("SELECT COUNT(*) FROM transfers t JOIN games g ON g.id = t.game_id WHERE t.is_paid = 0 AND g.status = 1"),
            ListSnapshots().Count);
    });

    private SqliteConnection OpenWithRecovery(string path)
    {
        var stopwatch = Stopwatch.StartNew();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Recovery = null;
        try
        {
            var connection = OpenAndPrepare(path);
            _log.DatabaseOpened(stopwatch.ElapsedMilliseconds, Migrations.Latest, DatabaseSizeBytes());
            return connection;
        }
        catch (Exception ex) when (ex is SqliteException or DatabaseUnusableException or IOException or InvalidOperationException)
        {
            var reason = ex is DatabaseUnusableException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            string? movedTo = null;
            try
            {
                movedTo = MoveAside(path);
                var fresh = OpenAndPrepare(path);
                Recovery = new RecoveryInfo(reason, movedTo, _clock.Now, InMemory: false);
                _log.DatabaseRecovered(ex, reason, movedTo ?? "(nothing to move)");
                return fresh;
            }
            catch (Exception second)
            {
                // Can't move or recreate the file: keep the app usable for this session only.
                Recovery = new RecoveryInfo($"{reason}; then {second.Message}", movedTo, _clock.Now, InMemory: true);
                _log.DatabaseRecovered(second, Recovery.Reason, movedTo ?? "(not moved)");
                return OpenAndPrepare(":memory:");
            }
        }
    }

    private SqliteConnection OpenAndPrepare(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = path == ":memory:" ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();
            var sql = new Sql(connection, null);
            // Reading the header first: a non-database file fails here with SQLITE_NOTADB.
            sql.Scalar("PRAGMA journal_mode = WAL;");
            sql.Execute("PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
            var check = sql.Query("PRAGMA quick_check;", r => r.GetString(0));
            if (check.Count != 1 || check[0] != "ok")
                throw new DatabaseUnusableException("Integrity check failed: " + string.Join("; ", check.Take(3)));
            Migrations.Apply(connection, _log);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Renames the file (and its -wal/-shm) to name.corrupt-yyyyMMdd-HHmmss. Never deletes.</summary>
    private string? MoveAside(string path)
    {
        if (!File.Exists(path)) return null;
        var stamp = _clock.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = $"{path}.corrupt-{stamp}";
        for (var n = 2; File.Exists(target); n++) target = $"{path}.corrupt-{stamp}-{n}";
        File.Move(path, target);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + suffix)) File.Move(path + suffix, target + suffix);
        }
        return target;
    }
}
