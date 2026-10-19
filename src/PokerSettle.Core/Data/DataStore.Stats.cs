using PokerSettle.Core.Stats;

namespace PokerSettle.Core.Data;

public sealed partial class DataStore
{
    /// <summary>
    /// The board: one indexed GROUP BY over the nets stored at finalization. Only finalized games count.
    /// </summary>
    public Task<IReadOnlyList<BoardRow>> GetBoardAsync(DateRange range, bool includeArchived) =>
        ReadAsync<IReadOnlyList<BoardRow>>(sql => QueryBoard(sql, range, includeArchived));

    /// <summary>A player's finalized games, oldest first (chart and history list).</summary>
    public Task<PlayerHistory?> GetPlayerHistoryAsync(long playerId) => ReadAsync(sql =>
    {
        var player = sql.QuerySingle($"SELECT {Rows.PlayerColumns} FROM players p WHERE p.id = $id", r => Rows.Player(r), ("$id", playerId));
        if (player is null) return null;
        var games = sql.Query(
            """
            SELECT g.id, g.date, g.title, e.net,
                   (SELECT COALESCE(SUM(b.amount), 0) FROM buy_ins b WHERE b.game_id = g.id AND b.player_id = e.player_id),
                   g.chip_mode
            FROM entries e
            JOIN games g ON g.id = e.game_id
            WHERE e.player_id = $id AND g.status = 1 AND e.net IS NOT NULL
            ORDER BY g.date, g.created_at, g.id
            """,
            r => new PlayerGameRow(r.GetInt64(0), r.GetDateOnly(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4), r.GetFlag(5)),
            ("$id", playerId));
        return new PlayerHistory(player, games);
    });

    private static List<BoardRow> QueryBoard(Sql sql, DateRange range, bool includeArchived) => sql.Query(
        $"""
        SELECT p.id, p.name, p.nickname, p.color, p.is_archived,
               COUNT(*)                                        AS games,
               SUM(e.net)                                      AS net,
               COALESCE(SUM(CASE WHEN e.net > 0 THEN e.net END), 0) AS won,
               COALESCE(SUM(CASE WHEN e.net < 0 THEN e.net END), 0) AS lost,
               SUM(CASE WHEN e.net > 0 THEN 1 ELSE 0 END)      AS wins,
               MAX(e.net)                                      AS best,
               MIN(e.net)                                      AS worst,
               MAX(g.date)                                     AS last_played
        FROM games g
        JOIN entries e ON e.game_id = g.id
        JOIN players p ON p.id = e.player_id
        WHERE g.status = 1 AND g.date >= $from AND g.date <= $to AND e.net IS NOT NULL
              {(includeArchived ? "" : "AND p.is_archived = 0")}
        GROUP BY e.player_id
        """,
        r =>
        {
            var best = r.GetInt64(10);
            var worst = r.GetInt64(11);
            return new BoardRow(r.GetInt64(0), r.GetString(1), r.GetStringOrNull(2), r.GetString(3), r.GetFlag(4),
                (int)r.GetInt64(5), r.GetInt64(6), r.GetInt64(7), r.GetInt64(8), (int)r.GetInt64(9),
                best > 0 ? best : null, worst < 0 ? worst : null, r.GetDateOnlyOrNull(12));
        },
        ("$from", range.From), ("$to", range.To));
}
