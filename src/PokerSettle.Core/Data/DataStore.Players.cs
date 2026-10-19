using Microsoft.Data.Sqlite;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Players;

namespace PokerSettle.Core.Data;

public sealed partial class DataStore
{
    private const int SqliteConstraintUnique = 2067;

    /// <summary>All players with finalized-game counts, ordered by name.</summary>
    public Task<IReadOnlyList<PlayerListItem>> GetPlayersAsync() => ReadAsync<IReadOnlyList<PlayerListItem>>(sql => sql.Query(
        $"""
        SELECT {Rows.PlayerColumns},
               COUNT(g.id), MAX(g.date)
        FROM players p
        LEFT JOIN entries e ON e.player_id = p.id
        LEFT JOIN games g ON g.id = e.game_id AND g.status = 1
        GROUP BY p.id
        ORDER BY p.name COLLATE NOCASE, p.id
        """,
        r => new PlayerListItem(Rows.Player(r), (int)r.GetInt64(9), r.GetDateOnlyOrNull(10))));

    public Task<Player?> GetPlayerAsync(long playerId) => ReadAsync(sql =>
        sql.QuerySingle($"SELECT {Rows.PlayerColumns} FROM players p WHERE p.id = $id", r => Rows.Player(r), ("$id", playerId)));

    public Task<Player?> FindPlayerByNameAsync(string name) => ReadAsync(sql =>
        sql.QuerySingle($"SELECT {Rows.PlayerColumns} FROM players p WHERE p.name_key = $key",
            r => Rows.Player(r), ("$key", PlayerNames.Key(name))));

    public Task<Player> AddPlayerAsync(PlayerDraft draft) => WriteAsync(sql => InsertPlayer(sql, draft));

    public Task<Player> UpdatePlayerAsync(Player player) => WriteAsync(sql =>
    {
        var name = ValidName(player.Name);
        try
        {
            var changed = sql.Execute(
                """
                UPDATE players SET name = $name, name_key = $key, nickname = $nick, phone = $phone, upi_id = $upi,
                       notes = $notes, color = $color, is_archived = $archived
                WHERE id = $id
                """,
                ("$name", name), ("$key", PlayerNames.Key(name)), ("$nick", Optional(player.Nickname)),
                ("$phone", Optional(player.Phone)), ("$upi", Optional(player.UpiId)), ("$notes", Optional(player.Notes)),
                ("$color", player.Color), ("$archived", player.IsArchived), ("$id", player.Id));
            if (changed == 0) throw new InvalidOperationException("Player not found.");
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == SqliteConstraintUnique)
        {
            throw new DuplicatePlayerNameException(name, ExistingId(sql, name));
        }
        _log.PlayerSaved(player.Id, false);
        return player with { Name = name };
    });

    /// <summary>True when the player appears in any game (they can then only be archived).</summary>
    public Task<bool> HasHistoryAsync(long playerId) => ReadAsync(sql =>
        sql.ScalarLong("SELECT EXISTS (SELECT 1 FROM entries WHERE player_id = $id)", ("$id", playerId)) != 0);

    public Task SetArchivedAsync(long playerId, bool archived) => WriteAsync(sql =>
    {
        sql.Execute("UPDATE players SET is_archived = $a WHERE id = $id", ("$a", archived), ("$id", playerId));
        _log.PlayerArchived(playerId, archived);
    });

    public Task DeletePlayerAsync(long playerId) => WriteAsync(sql =>
    {
        if (sql.ScalarLong("SELECT EXISTS (SELECT 1 FROM entries WHERE player_id = $id)", ("$id", playerId)) != 0)
            throw new InvalidOperationException("Players with game history can only be archived.");
        sql.Execute("DELETE FROM players WHERE id = $id", ("$id", playerId));
        _log.PlayerDeleted(playerId);
    });

    /// <summary>
    /// Players that could join a game, with the ranking signals: in the most recent game, count in the
    /// last 10 games, and last played date. One query (no per-player round trips).
    /// </summary>
    public Task<IReadOnlyList<SuggestionCandidate>> GetSuggestionCandidatesAsync(long? excludeGameId = null) =>
        ReadAsync<IReadOnlyList<SuggestionCandidate>>(sql => sql.Query(
            """
            WITH recent AS (
                SELECT id, ROW_NUMBER() OVER (ORDER BY date DESC, created_at DESC, id DESC) AS rn
                FROM games WHERE id <> $exclude
                ORDER BY date DESC, created_at DESC, id DESC
                LIMIT 10
            )
            SELECT p.id, p.name, p.nickname, p.color,
                   EXISTS (SELECT 1 FROM entries e JOIN recent r ON r.id = e.game_id AND r.rn = 1 WHERE e.player_id = p.id),
                   (SELECT COUNT(*) FROM entries e JOIN recent r ON r.id = e.game_id WHERE e.player_id = p.id),
                   (SELECT MAX(g.date) FROM entries e JOIN games g ON g.id = e.game_id WHERE e.player_id = p.id AND g.id <> $exclude)
            FROM players p
            WHERE p.is_archived = 0
            """,
            r => new SuggestionCandidate(r.GetInt64(0), r.GetString(1), r.GetStringOrNull(2), r.GetString(3),
                r.GetFlag(4), (int)r.GetInt64(5), r.GetDateOnlyOrNull(6)),
            ("$exclude", excludeGameId ?? -1)));

    /// <summary>Player ids from the most recent game (excluding one game), in seat order.</summary>
    public Task<IReadOnlyList<long>> GetLastGamePlayerIdsAsync(long? excludeGameId = null) =>
        ReadAsync<IReadOnlyList<long>>(sql => sql.Query(
            """
            SELECT e.player_id FROM entries e
            JOIN players p ON p.id = e.player_id
            WHERE p.is_archived = 0 AND e.game_id = (
                SELECT id FROM games WHERE id <> $exclude ORDER BY date DESC, created_at DESC, id DESC LIMIT 1)
            ORDER BY e.position, e.player_id
            """,
            r => r.GetInt64(0), ("$exclude", excludeGameId ?? -1)));

    /// <summary>Settings of the most recent game, used to prefill a new game.</summary>
    public Task<GameSettings?> GetLastGameSettingsAsync() => ReadAsync(sql =>
        sql.QuerySingle($"SELECT {Rows.GameColumns} FROM games g ORDER BY g.date DESC, g.created_at DESC, g.id DESC LIMIT 1",
            r => Rows.Game(r).Settings));

    /// <summary>Phase 5: moves all history from <paramref name="sourceId"/> to <paramref name="targetId"/> and deletes the source.</summary>
    public Task MergePlayersAsync(long sourceId, long targetId) => WriteAsync(sql =>
    {
        if (sourceId == targetId) throw new ArgumentException("Pick two different players.");
        var shared = sql.ScalarLong(
            "SELECT COUNT(*) FROM entries a JOIN entries b ON a.game_id = b.game_id WHERE a.player_id = $s AND b.player_id = $t",
            ("$s", sourceId), ("$t", targetId));
        if (shared > 0) throw new InvalidOperationException("These players were in the same game, so they can't be the same person.");

        // Entries are the parent of buy_ins (composite key), so copy then delete.
        sql.Execute("INSERT INTO entries (game_id, player_id, position, cash_out, net) SELECT game_id, $t, position, cash_out, net FROM entries WHERE player_id = $s",
            ("$s", sourceId), ("$t", targetId));
        sql.Execute("UPDATE buy_ins SET player_id = $t WHERE player_id = $s", ("$s", sourceId), ("$t", targetId));
        sql.Execute("DELETE FROM entries WHERE player_id = $s", ("$s", sourceId));
        sql.Execute("UPDATE adjustments SET player_id = $t WHERE player_id = $s", ("$s", sourceId), ("$t", targetId));
        sql.Execute("UPDATE transfers SET from_id = $t WHERE from_id = $s", ("$s", sourceId), ("$t", targetId));
        sql.Execute("UPDATE transfers SET to_id = $t WHERE to_id = $s", ("$s", sourceId), ("$t", targetId));
        sql.Execute("UPDATE games SET banker_id = $t WHERE banker_id = $s", ("$s", sourceId), ("$t", targetId));
        sql.Execute("DELETE FROM players WHERE id = $s", ("$s", sourceId));
        _log.PlayersMerged(sourceId, targetId);
    });

    private Player InsertPlayer(Sql sql, PlayerDraft draft)
    {
        var name = ValidName(draft.Name);
        var color = string.IsNullOrWhiteSpace(draft.Color) ? PlayerNames.ColorFor(name) : draft.Color;
        var now = _clock.Now;
        long id;
        try
        {
            id = sql.Insert(
                """
                INSERT INTO players (name, name_key, nickname, phone, upi_id, notes, color, is_archived, created_at)
                VALUES ($name, $key, $nick, $phone, $upi, $notes, $color, 0, $created)
                RETURNING id
                """,
                ("$name", name), ("$key", PlayerNames.Key(name)), ("$nick", Optional(draft.Nickname)),
                ("$phone", Optional(draft.Phone)), ("$upi", Optional(draft.UpiId)), ("$notes", Optional(draft.Notes)),
                ("$color", color), ("$created", now));
        }
        catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == SqliteConstraintUnique)
        {
            throw new DuplicatePlayerNameException(name, ExistingId(sql, name));
        }
        _log.PlayerSaved(id, true);
        return new Player
        {
            Id = id,
            Name = name,
            Nickname = Optional(draft.Nickname),
            Phone = Optional(draft.Phone),
            UpiId = Optional(draft.UpiId),
            Notes = Optional(draft.Notes),
            Color = color,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(now.ToUnixTimeMilliseconds()),
        };
    }

    private static long ExistingId(Sql sql, string name) =>
        sql.ScalarLong("SELECT id FROM players WHERE name_key = $key", ("$key", PlayerNames.Key(name)));

    private static string ValidName(string? raw)
    {
        var name = PlayerNames.Clean(raw);
        if (name.Length == 0) throw new ArgumentException("Name is required.");
        if (name.Length > PlayerNames.MaxNameLength) throw new ArgumentException("Name is too long.");
        return name;
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
