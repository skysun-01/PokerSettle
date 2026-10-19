using System.Diagnostics;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Players;
using PokerSettle.Core.Settlement;

namespace PokerSettle.Core.Data;

public sealed partial class DataStore
{
    /// <summary>Reads every player and game (6 queries total, grouped in memory).</summary>
    public Task<(IReadOnlyList<Player> Players, IReadOnlyList<GameRecord> Games)> ReadEverythingAsync() => ReadAsync(sql =>
    {
        var players = sql.Query($"SELECT {Rows.PlayerColumns} FROM players p ORDER BY p.id", r => Rows.Player(r));
        var games = sql.Query($"SELECT {Rows.GameColumns} FROM games g ORDER BY g.id", r => Rows.Game(r));
        var entries = sql.Query($"SELECT {Rows.EntryColumns} FROM entries e ORDER BY e.game_id, e.position, e.player_id", r => Rows.Entry(r))
            .ToLookup(e => e.GameId);
        var buyIns = sql.Query($"SELECT {Rows.BuyInColumns} FROM buy_ins b ORDER BY b.id", Rows.BuyIn).ToLookup(b => b.GameId);
        var adjustments = sql.Query($"SELECT {Rows.AdjustmentColumns} FROM adjustments a ORDER BY a.id", Rows.Adjustment).ToLookup(a => a.GameId);
        var transfers = sql.Query($"SELECT {Rows.TransferColumns} FROM transfers t ORDER BY t.id", Rows.Transfer).ToLookup(t => t.GameId);
        var records = games
            .Select(g => new GameRecord(g, entries[g.Id].ToList(), buyIns[g.Id].ToList(), adjustments[g.Id].ToList(), transfers[g.Id].ToList()))
            .ToList();
        return ((IReadOnlyList<Player>)players, (IReadOnlyList<GameRecord>)records);
    });

    /// <summary>
    /// Replaces all data in one transaction, after saving a snapshot of the current database.
    /// Nets of finalized games are recomputed from their rows so they always match the data.
    /// </summary>
    public Task ReplaceEverythingAsync(IReadOnlyList<Player> players, IReadOnlyList<GameRecord> games) =>
        ReplaceEverythingCoreAsync(players, games, snapshotReason: "before-import");

    /// <summary>Deletes all players and games after saving a snapshot.</summary>
    public Task ClearEverythingAsync() => ReplaceEverythingCoreAsync([], [], snapshotReason: "before-clear");

    /// <summary>Bulk load without a snapshot: demo database and performance tests only.</summary>
    internal Task SeedAsync(IReadOnlyList<Player> players, IReadOnlyList<GameRecord> games) =>
        ReplaceEverythingCoreAsync(players, games, snapshotReason: null);

    public Task SeedDemoDataAsync(int playerCount = 40, int gameCount = 2000, int seed = 42)
    {
        var (players, games) = DemoData.Generate(playerCount, gameCount, seed, _clock.Today);
        return SeedAsync(players, games);
    }

    private Task ReplaceEverythingCoreAsync(IReadOnlyList<Player> players, IReadOnlyList<GameRecord> games, string? snapshotReason) =>
        RunAsync(sql =>
        {
            var stopwatch = Stopwatch.StartNew();
            if (snapshotReason is not null) CreateSnapshot(sql, snapshotReason);

            using var tx = sql.Connection.BeginTransaction();
            var w = new Sql(sql.Connection, tx);
            w.Execute("DELETE FROM games; DELETE FROM players;");

            using (var insert = w.Command(
                """
                INSERT INTO players (id, name, name_key, nickname, phone, upi_id, notes, color, is_archived, created_at)
                VALUES ($id, $name, $key, $nick, $phone, $upi, $notes, $color, $archived, $created)
                """))
            {
                foreach (var p in players)
                {
                    var name = PlayerNames.Clean(p.Name);
                    Bind(insert, ("$id", p.Id), ("$name", name), ("$key", PlayerNames.Key(name)), ("$nick", p.Nickname),
                        ("$phone", p.Phone), ("$upi", p.UpiId), ("$notes", p.Notes), ("$color", p.Color),
                        ("$archived", p.IsArchived), ("$created", p.CreatedAt));
                    insert.ExecuteNonQuery();
                }
            }

            foreach (var record in games) InsertGameRecord(w, record);

            foreach (var (gameId, loaded) in GameLines.LoadForFinalizedGames(w))
            {
                var nets = SettlementEngine.MoneyNets(loaded.Lines, loaded.ChipMode, loaded.ChipRate);
                using var update = w.Command("UPDATE entries SET net = $net WHERE game_id = $g AND player_id = $p");
                for (var i = 0; i < nets.Length; i++)
                {
                    Bind(update, ("$net", nets[i]), ("$g", gameId), ("$p", loaded.Lines[i].PlayerId));
                    update.ExecuteNonQuery();
                }
            }
            tx.Commit();

            if (snapshotReason == "before-clear") _log.DataCleared();
            else _log.Timing($"Replace data ({players.Count} players, {games.Count} games)", stopwatch.ElapsedMilliseconds);
            return true;
        }, transaction: false);
}
