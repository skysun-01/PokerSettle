using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Settlement;

namespace PokerSettle.Core.Data;

/// <summary>
/// Schema versions tracked in PRAGMA user_version. Each migration runs in its own transaction
/// together with the version bump, so a failed migration leaves the previous version intact.
/// Never edit a shipped migration; add a new one.
/// </summary>
internal static class Migrations
{
    public const int Latest = 2;

    private sealed record Migration(int Version, string Description, Action<Sql, ILogger> Apply);

    private static readonly Migration[] All =
    [
        new(1, "Initial schema", (sql, _) => sql.Execute(V1)),
        new(2, "Store each player's net on entries; board indexes", (sql, log) =>
        {
            sql.Execute(V2);
            BackfillNets(sql, log);
        }),
    ];

    public static int GetVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Upgrades to <paramref name="targetVersion"/> (default: latest). Tests use the target to build old databases.</summary>
    public static void Apply(SqliteConnection connection, ILogger log, int targetVersion = Latest)
    {
        var current = GetVersion(connection);
        if (current > Latest)
            throw new DatabaseUnusableException($"The data was saved by a newer version of Poker Settle (schema {current}; this app supports {Latest}).");

        foreach (var migration in All)
        {
            if (migration.Version <= current || migration.Version > targetVersion) continue;
            var stopwatch = Stopwatch.StartNew();
            using var transaction = connection.BeginTransaction();
            var sql = new Sql(connection, transaction);
            migration.Apply(sql, log);
            sql.Execute($"PRAGMA user_version = {migration.Version};");
            transaction.Commit();
            log.MigrationApplied(migration.Version, migration.Description, stopwatch.ElapsedMilliseconds);
        }
    }

    private const string V1 = """
        CREATE TABLE players (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            name        TEXT    NOT NULL,
            name_key    TEXT    NOT NULL,
            nickname    TEXT,
            phone       TEXT,
            upi_id      TEXT,
            notes       TEXT,
            color       TEXT    NOT NULL,
            is_archived INTEGER NOT NULL DEFAULT 0,
            created_at  INTEGER NOT NULL
        );
        CREATE UNIQUE INDEX ux_players_name_key ON players(name_key);

        CREATE TABLE games (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            title             TEXT    NOT NULL,
            date              TEXT    NOT NULL,
            location          TEXT,
            notes             TEXT,
            status            INTEGER NOT NULL DEFAULT 0,
            default_buy_in    INTEGER NOT NULL,
            chip_mode         INTEGER NOT NULL DEFAULT 0,
            chip_rate_chips   INTEGER,
            chip_rate_money   INTEGER,
            settlement_mode   INTEGER NOT NULL DEFAULT 0,
            banker_id         INTEGER REFERENCES players(id),
            rounding          INTEGER NOT NULL DEFAULT 0,
            start_with_buy_in INTEGER NOT NULL DEFAULT 1,
            created_at        INTEGER NOT NULL,
            updated_at        INTEGER NOT NULL,
            finalized_at      INTEGER
        );
        CREATE INDEX ix_games_date ON games(date, created_at);
        -- Only one game can be in progress at a time.
        CREATE UNIQUE INDEX ux_games_one_in_progress ON games(status) WHERE status = 0;

        CREATE TABLE entries (
            game_id   INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
            player_id INTEGER NOT NULL REFERENCES players(id),
            position  INTEGER NOT NULL,
            cash_out  INTEGER CHECK (cash_out IS NULL OR cash_out >= 0),
            PRIMARY KEY (game_id, player_id)
        ) WITHOUT ROWID;

        CREATE TABLE buy_ins (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            game_id   INTEGER NOT NULL,
            player_id INTEGER NOT NULL,
            amount    INTEGER NOT NULL CHECK (amount > 0),
            at        INTEGER NOT NULL,
            FOREIGN KEY (game_id, player_id) REFERENCES entries(game_id, player_id) ON DELETE CASCADE
        );
        CREATE INDEX ix_buy_ins_game_player ON buy_ins(game_id, player_id);

        CREATE TABLE adjustments (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            game_id   INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
            kind      INTEGER NOT NULL,
            player_id INTEGER REFERENCES players(id),
            amount    INTEGER NOT NULL
        );
        CREATE INDEX ix_adjustments_game ON adjustments(game_id);

        CREATE TABLE transfers (
            id      INTEGER PRIMARY KEY AUTOINCREMENT,
            game_id INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
            from_id INTEGER NOT NULL REFERENCES players(id),
            to_id   INTEGER NOT NULL REFERENCES players(id),
            amount  INTEGER NOT NULL CHECK (amount > 0),
            is_paid INTEGER NOT NULL DEFAULT 0,
            paid_at INTEGER
        );
        CREATE INDEX ix_transfers_game ON transfers(game_id);
        CREATE INDEX ix_transfers_from ON transfers(from_id);
        CREATE INDEX ix_transfers_to ON transfers(to_id);
        """;

    private const string V2 = """
        ALTER TABLE entries ADD COLUMN net INTEGER;
        CREATE INDEX ix_games_status_date ON games(status, date);
        CREATE INDEX ix_entries_player ON entries(player_id, game_id);
        CREATE INDEX ix_transfers_unpaid ON transfers(game_id) WHERE is_paid = 0;
        """;

    /// <summary>v2: compute and store nets for games finalized before nets were stored.</summary>
    private static void BackfillNets(Sql sql, ILogger log)
    {
        var lines = GameLines.LoadForFinalizedGames(sql);
        foreach (var (gameId, game) in lines)
        {
            var issues = SettlementEngine.Validate(game.Lines, new SettlementOptions(game.ChipMode, game.ChipRate,
                Models.SettlementMode.FewestPayments, null, Models.RoundingMode.None));
            if (issues.Any(i => i.IsBlocking))
            {
                log.ValidationFailed("migration v2", $"game {gameId} can't be recomputed; net left empty");
                continue;
            }
            var nets = SettlementEngine.MoneyNets(game.Lines, game.ChipMode, game.ChipRate);
            for (var i = 0; i < game.Lines.Count; i++)
            {
                sql.Execute("UPDATE entries SET net = $net WHERE game_id = $g AND player_id = $p",
                    ("$net", nets[i]), ("$g", gameId), ("$p", game.Lines[i].PlayerId));
            }
        }
    }
}

/// <summary>Loads settlement lines (buy-in totals, cash-outs, adjustments) straight from SQL.</summary>
internal static class GameLines
{
    public sealed record Loaded(bool ChipMode, Models.ChipRate? ChipRate, List<PlayerLine> Lines);

    private const string LineSelect = """
        SELECT e.game_id, e.player_id, e.position, e.cash_out,
               (SELECT COALESCE(SUM(b.amount), 0) FROM buy_ins b WHERE b.game_id = e.game_id AND b.player_id = e.player_id),
               (SELECT COUNT(*) FROM buy_ins b WHERE b.game_id = e.game_id AND b.player_id = e.player_id),
               (SELECT COALESCE(SUM(a.amount), 0) FROM adjustments a WHERE a.game_id = e.game_id AND a.player_id = e.player_id),
               g.chip_mode, g.chip_rate_chips, g.chip_rate_money
        FROM entries e
        JOIN games g ON g.id = e.game_id
        """;

    public static Loaded? LoadForGame(Sql sql, long gameId)
    {
        var map = Load(sql, LineSelect + " WHERE e.game_id = $g ORDER BY e.player_id", ("$g", gameId));
        return map.TryGetValue(gameId, out var loaded) ? loaded : null;
    }

    public static Dictionary<long, Loaded> LoadForFinalizedGames(Sql sql) =>
        Load(sql, LineSelect + " WHERE g.status = 1 ORDER BY e.game_id, e.player_id");

    private static Dictionary<long, Loaded> Load(Sql sql, string text, params (string, object?)[] args)
    {
        var result = new Dictionary<long, Loaded>();
        sql.Query(text, r =>
        {
            var gameId = r.GetInt64(0);
            if (!result.TryGetValue(gameId, out var loaded))
            {
                var chips = r.GetInt64OrNull(8);
                var money = r.GetInt64OrNull(9);
                loaded = new Loaded(r.GetFlag(7), chips is > 0 && money is > 0 ? new Models.ChipRate(chips.Value, money.Value) : null, []);
                result[gameId] = loaded;
            }
            loaded.Lines.Add(new PlayerLine(r.GetInt64(1), (int)r.GetInt64(2), r.GetInt64(4), (int)r.GetInt64(5),
                r.GetInt64OrNull(3), r.GetInt64(6)));
            return 0;
        }, args);
        return result;
    }
}
