using System.Globalization;
using Microsoft.Data.Sqlite;
using PokerSettle.Core.Logging;
using PokerSettle.Core.Models;
using PokerSettle.Core.Settlement;
using PokerSettle.Core.Stats;

namespace PokerSettle.Core.Data;

public sealed partial class DataStore
{
    public Task<long> CreateGameAsync(NewGameRequest request) => WriteAsync(sql =>
    {
        if (request.PlayerIds.Count == 0) throw new ArgumentException("Pick at least one player.");
        if (request.PlayerIds.Distinct().Count() != request.PlayerIds.Count) throw new ArgumentException("A player can only be added once.");
        if (request.Settings.DefaultBuyIn <= 0) throw new ArgumentException("The default buy-in must be more than 0.");
        if (request.Settings.ChipMode && request.Settings.ChipRate is not { IsValid: true })
            throw new ArgumentException("Chip mode needs a chip rate.");

        if (sql.ScalarLongOrNull("SELECT id FROM games WHERE status = 0 LIMIT 1") is { } existing)
            throw new GameInProgressException(existing);

        var now = _clock.Now;
        var s = request.Settings;
        var gameId = sql.Insert(
            """
            INSERT INTO games (title, date, location, notes, status, default_buy_in, chip_mode, chip_rate_chips, chip_rate_money,
                               settlement_mode, banker_id, rounding, start_with_buy_in, created_at, updated_at)
            VALUES ($title, $date, $loc, $notes, 0, $buyin, $chip, $rc, $rm, $mode, $banker, $round, $start, $now, $now)
            RETURNING id
            """,
            ("$title", request.Title.Trim()), ("$date", request.Date), ("$loc", Optional(request.Location)),
            ("$notes", Optional(request.Notes)), ("$buyin", s.DefaultBuyIn), ("$chip", s.ChipMode),
            ("$rc", s.ChipMode ? s.ChipRate?.Chips : null), ("$rm", s.ChipMode ? s.ChipRate?.Money : null),
            ("$mode", s.SettlementMode), ("$banker", s.BankerId), ("$round", s.Rounding), ("$start", s.StartWithBuyIn),
            ("$now", now));

        var position = 0;
        foreach (var playerId in request.PlayerIds)
        {
            InsertEntry(sql, gameId, playerId, position++);
            if (s.StartWithBuyIn) InsertBuyIn(sql, gameId, playerId, s.DefaultBuyIn, now);
        }
        _log.GameCreated(gameId, request.PlayerIds.Count, s.ChipMode, s.DefaultBuyIn);
        return gameId;
    });

    public Task<GameSnapshot?> GetGameAsync(long gameId) => ReadAsync(sql => LoadSnapshot(sql, gameId));

    public Task<long?> GetInProgressGameIdAsync() => ReadAsync(sql => sql.ScalarLongOrNull("SELECT id FROM games WHERE status = 0 LIMIT 1"));

    public Task UpdateGameDetailsAsync(long gameId, string title, DateOnly date, string? location, string? notes) => WriteAsync(sql =>
    {
        sql.Execute("UPDATE games SET title = $t, date = $d, location = $l, notes = $n, updated_at = $now WHERE id = $id",
            ("$t", title.Trim()), ("$d", date), ("$l", Optional(location)), ("$n", Optional(notes)), ("$now", _clock.Now), ("$id", gameId));
        _log.GameUpdated(gameId);
    });

    public Task<BuyIn> AddBuyInAsync(long gameId, long playerId, long amount) => WriteAsync(sql =>
    {
        if (amount <= 0) throw new ArgumentException("A buy-in must be more than 0.");
        EnsureInProgress(sql, gameId);
        var buyIn = InsertBuyIn(sql, gameId, playerId, amount, _clock.Now);
        Touch(sql, gameId);
        _log.BuyInAdded(gameId, playerId, amount, buyIn.Id);
        return buyIn;
    });

    /// <summary>Removes a buy-in and returns it so Undo can restore it.</summary>
    public Task<BuyIn?> RemoveBuyInAsync(long buyInId) => WriteAsync(sql =>
    {
        var buyIn = sql.QuerySingle($"SELECT {Rows.BuyInColumns} FROM buy_ins b WHERE b.id = $id", Rows.BuyIn, ("$id", buyInId));
        if (buyIn is null) return null;
        EnsureInProgress(sql, buyIn.GameId);
        sql.Execute("DELETE FROM buy_ins WHERE id = $id", ("$id", buyInId));
        Touch(sql, buyIn.GameId);
        _log.BuyInRemoved(buyIn.GameId, buyInId);
        return buyIn;
    });

    public Task RestoreBuyInAsync(BuyIn buyIn) => WriteAsync(sql =>
    {
        EnsureInProgress(sql, buyIn.GameId);
        sql.Execute("INSERT INTO buy_ins (id, game_id, player_id, amount, at) VALUES ($id, $g, $p, $a, $at)",
            ("$id", buyIn.Id), ("$g", buyIn.GameId), ("$p", buyIn.PlayerId), ("$a", buyIn.Amount), ("$at", buyIn.At));
        Touch(sql, buyIn.GameId);
        _log.UndoPerformed("buy-in restored");
    });

    /// <summary>Saves (or clears, with null) a player's cash-out in game units.</summary>
    public Task SetCashOutAsync(long gameId, long playerId, long? amount) => WriteAsync(sql =>
    {
        if (amount < 0) throw new ArgumentException("A cash-out can't be negative.");
        EnsureInProgress(sql, gameId);
        sql.Execute("UPDATE entries SET cash_out = $a WHERE game_id = $g AND player_id = $p",
            ("$a", amount), ("$g", gameId), ("$p", playerId));
        Touch(sql, gameId);
        _log.CashOutSet(gameId, playerId, amount);
    });

    /// <summary>Adds a late player (with the default buy-in if the game starts everyone with one).</summary>
    public Task<BuyIn?> AddPlayerToGameAsync(long gameId, long playerId) => WriteAsync(sql =>
    {
        var game = EnsureInProgress(sql, gameId);
        if (sql.ScalarLong("SELECT EXISTS (SELECT 1 FROM entries WHERE game_id = $g AND player_id = $p)", ("$g", gameId), ("$p", playerId)) != 0)
            return null;
        var position = (int)sql.ScalarLong("SELECT COALESCE(MAX(position) + 1, 0) FROM entries WHERE game_id = $g", ("$g", gameId));
        InsertEntry(sql, gameId, playerId, position);
        BuyIn? buyIn = null;
        if (game.Settings.StartWithBuyIn) buyIn = InsertBuyIn(sql, gameId, playerId, game.Settings.DefaultBuyIn, _clock.Now);
        Touch(sql, gameId);
        _log.PlayerJoinedGame(gameId, playerId);
        return buyIn;
    });

    /// <summary>Removes a player (and their buy-ins) from a live game; returns what Undo needs.</summary>
    public Task<RemovedEntry?> RemovePlayerFromGameAsync(long gameId, long playerId) => WriteAsync(sql =>
    {
        EnsureInProgress(sql, gameId);
        var entry = sql.QuerySingle($"SELECT {Rows.EntryColumns} FROM entries e WHERE e.game_id = $g AND e.player_id = $p",
            r => Rows.Entry(r), ("$g", gameId), ("$p", playerId));
        if (entry is null) return null;
        var buyIns = sql.Query($"SELECT {Rows.BuyInColumns} FROM buy_ins b WHERE b.game_id = $g AND b.player_id = $p ORDER BY b.id",
            Rows.BuyIn, ("$g", gameId), ("$p", playerId));
        sql.Execute("DELETE FROM entries WHERE game_id = $g AND player_id = $p", ("$g", gameId), ("$p", playerId));
        Touch(sql, gameId);
        _log.PlayerLeftGame(gameId, playerId);
        return new RemovedEntry(entry, buyIns);
    });

    public Task RestorePlayerInGameAsync(RemovedEntry removed) => WriteAsync(sql =>
    {
        var e = removed.Entry;
        EnsureInProgress(sql, e.GameId);
        sql.Execute("INSERT INTO entries (game_id, player_id, position, cash_out, net) VALUES ($g, $p, $pos, $c, NULL)",
            ("$g", e.GameId), ("$p", e.PlayerId), ("$pos", e.Position), ("$c", e.CashOut));
        foreach (var b in removed.BuyIns)
        {
            sql.Execute("INSERT INTO buy_ins (id, game_id, player_id, amount, at) VALUES ($id, $g, $p, $a, $at)",
                ("$id", b.Id), ("$g", b.GameId), ("$p", b.PlayerId), ("$a", b.Amount), ("$at", b.At));
        }
        Touch(sql, e.GameId);
        _log.UndoPerformed("player restored to game");
    });

    /// <summary>
    /// Finalizes in one transaction: resolves any discrepancy as stored adjustments, computes the
    /// settlement, writes each player's net on their entry, replaces the payments (keeping paid marks
    /// for identical payments from an earlier finalization) and marks the game finalized.
    /// </summary>
    public Task<SettlementResult> FinalizeGameAsync(long gameId, FinalizeRequest request) => WriteAsync(sql =>
    {
        var game = EnsureInProgress(sql, gameId);
        sql.Execute("DELETE FROM adjustments WHERE game_id = $g", ("$g", gameId));

        var loaded = GameLines.LoadForGame(sql, gameId) ?? throw new InvalidOperationException("The game has no players.");
        var lines = (IReadOnlyList<PlayerLine>)loaded.Lines;
        var options = new SettlementOptions(game.Settings.ChipMode, game.Settings.ChipRate, request.Mode, request.BankerId, request.Rounding);

        var imbalance = SettlementEngine.Imbalance(lines);
        var preIssues = SettlementEngine.Validate(lines, options).Where(i => i.IsBlocking && i.Kind != IssueKind.Unbalanced).ToList();
        if (preIssues.Count > 0) throw new SettlementValidationException(preIssues);

        if (imbalance != 0)
        {
            if (request.Resolution == DiscrepancyResolution.None)
                throw new SettlementValidationException([new SettlementIssue(IssueKind.Unbalanced, Amount: imbalance)]);
            var shares = SettlementEngine.ResolveDiscrepancy(lines, request.Resolution, request.AssignedPlayerId);
            var kind = request.Resolution == DiscrepancyResolution.SplitEqually ? AdjustmentKind.SplitDifference : AdjustmentKind.AssignedDifference;
            foreach (var share in shares)
            {
                sql.Execute("INSERT INTO adjustments (game_id, kind, player_id, amount) VALUES ($g, $k, $p, $a)",
                    ("$g", gameId), ("$k", kind), ("$p", share.PlayerId), ("$a", share.Amount));
            }
            lines = SettlementEngine.ApplyAdjustments(lines, shares);
        }

        var result = SettlementEngine.Settle(lines, options);
        var now = _clock.Now;

        foreach (var r in result.Results)
        {
            sql.Execute("UPDATE entries SET net = $net WHERE game_id = $g AND player_id = $p",
                ("$net", r.Net), ("$g", gameId), ("$p", r.PlayerId));
        }

        var previous = sql.Query($"SELECT {Rows.TransferColumns} FROM transfers t WHERE t.game_id = $g", Rows.Transfer, ("$g", gameId));
        sql.Execute("DELETE FROM transfers WHERE game_id = $g", ("$g", gameId));
        foreach (var p in result.Payments)
        {
            var match = previous.FirstOrDefault(t => t.IsPaid && t.FromId == p.FromId && t.ToId == p.ToId && t.Amount == p.Amount);
            if (match is not null) previous.Remove(match);
            sql.Execute("INSERT INTO transfers (game_id, from_id, to_id, amount, is_paid, paid_at) VALUES ($g, $f, $t, $a, $paid, $at)",
                ("$g", gameId), ("$f", p.FromId), ("$t", p.ToId), ("$a", p.Amount), ("$paid", match is not null), ("$at", match?.PaidAt));
        }

        sql.Execute(
            """
            UPDATE games SET status = 1, settlement_mode = $mode, banker_id = $banker, rounding = $round,
                   finalized_at = $now, updated_at = $now
            WHERE id = $g
            """,
            ("$mode", request.Mode), ("$banker", request.Mode == SettlementMode.Banker ? request.BankerId : null),
            ("$round", request.Rounding), ("$now", now), ("$g", gameId));

        _log.SettlementComputed(gameId, request.Mode.ToString(), request.Rounding.ToString(), game.Settings.ChipMode,
            string.Join(";", result.Results.Select(r => $"{r.PlayerId}:{r.Net}")),
            string.Join(";", result.Payments.Select(p => $"{p.FromId}->{p.ToId}:{p.Amount}")));
        _log.GameFinalized(gameId, result.Payments.Count, request.Resolution.ToString());
        return result;
    });

    /// <summary>
    /// Back to in progress. Nets and adjustments are cleared (recomputed at the next finalize);
    /// payments stay hidden so identical ones keep their paid marks.
    /// </summary>
    public Task ReopenGameAsync(long gameId) => WriteAsync(sql =>
    {
        var other = sql.ScalarLongOrNull("SELECT id FROM games WHERE status = 0 AND id <> $g LIMIT 1", ("$g", gameId));
        if (other is { } otherId) throw new GameInProgressException(otherId);
        sql.Execute("UPDATE games SET status = 0, finalized_at = NULL, updated_at = $now WHERE id = $g", ("$now", _clock.Now), ("$g", gameId));
        sql.Execute("UPDATE entries SET net = NULL WHERE game_id = $g", ("$g", gameId));
        sql.Execute("DELETE FROM adjustments WHERE game_id = $g", ("$g", gameId));
        _log.GameReopened(gameId);
    });

    /// <summary>Deletes a game and returns everything needed to restore it (Undo).</summary>
    public Task<GameRecord?> DeleteGameAsync(long gameId) => WriteAsync(sql =>
    {
        var snapshot = LoadSnapshot(sql, gameId);
        if (snapshot is null) return null;
        sql.Execute("DELETE FROM games WHERE id = $g", ("$g", gameId));
        _log.GameDeleted(gameId);
        return GameRecord.From(snapshot);
    });

    public Task RestoreGameAsync(GameRecord record) => WriteAsync(sql =>
    {
        if (record.Game.Status == GameStatus.InProgress &&
            sql.ScalarLongOrNull("SELECT id FROM games WHERE status = 0 LIMIT 1") is { } existing)
            throw new GameInProgressException(existing);
        InsertGameRecord(sql, record);
        _log.GameRestored(record.Game.Id);
    });

    public Task<Transfer?> SetTransferPaidAsync(long transferId, bool paid) => WriteAsync(sql =>
    {
        DateTimeOffset? at = paid ? _clock.Now : null;
        sql.Execute("UPDATE transfers SET is_paid = $p, paid_at = $at WHERE id = $id", ("$p", paid), ("$at", at), ("$id", transferId));
        _log.TransferPaid(transferId, paid);
        return sql.QuerySingle($"SELECT {Rows.TransferColumns} FROM transfers t WHERE t.id = $id", Rows.Transfer, ("$id", transferId));
    });

    /// <summary>Games history: newest first, with player count, pot, top winner and unpaid count.</summary>
    public Task<IReadOnlyList<GameListItem>> GetGamesAsync(GameQuery query) =>
        ReadAsync<IReadOnlyList<GameListItem>>(sql => QueryGames(sql, query));

    public Task<IReadOnlyList<PendingPayment>> GetPendingPaymentsAsync() =>
        ReadAsync<IReadOnlyList<PendingPayment>>(QueryPending);

    /// <summary>Everything the Home screen shows, in one round trip.</summary>
    public Task<HomeData> GetHomeAsync(DateTimeOffset? lastBackupAt) => ReadAsync(sql =>
    {
        var inProgress = sql.QuerySingle(
            """
            SELECT g.id, g.title, g.date, g.chip_mode, g.chip_rate_chips, g.chip_rate_money,
                   (SELECT COUNT(*) FROM entries e WHERE e.game_id = g.id),
                   (SELECT COALESCE(SUM(b.amount), 0) FROM buy_ins b WHERE b.game_id = g.id)
            FROM games g WHERE g.status = 0 LIMIT 1
            """,
            r =>
            {
                var chips = r.GetInt64OrNull(4);
                var money = r.GetInt64OrNull(5);
                return new InProgressGame(r.GetInt64(0), r.GetString(1), r.GetDateOnly(2), r.GetFlag(3),
                    chips is > 0 && money is > 0 ? new ChipRate(chips.Value, money.Value) : null, (int)r.GetInt64(6), r.GetInt64(7));
            });
        var recent = QueryGames(sql, new GameQuery(Limit: 5));
        var board = QueryBoard(sql, DateRange.All, includeArchived: false);
        var pending = QueryPending(sql);
        var finalized = (int)sql.ScalarLong("SELECT COUNT(*) FROM games WHERE status = 1");
        var sinceBackup = lastBackupAt is { } since
            ? (int)sql.ScalarLong("SELECT COUNT(*) FROM games WHERE status = 1 AND finalized_at > $since", ("$since", since))
            : finalized;
        var players = (int)sql.ScalarLong("SELECT COUNT(*) FROM players WHERE is_archived = 0");
        return new HomeData(inProgress, recent, board, pending, finalized, sinceBackup, players);
    });

    private static List<GameListItem> QueryGames(Sql sql, GameQuery query)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : "%" + EscapeLike(query.Search.Trim()) + "%";
        return sql.Query(
            """
            SELECT g.id, g.title, g.date, g.status, g.chip_mode, g.chip_rate_chips, g.chip_rate_money,
                   (SELECT COUNT(*) FROM entries e WHERE e.game_id = g.id),
                   (SELECT COALESCE(SUM(b.amount), 0) FROM buy_ins b WHERE b.game_id = g.id),
                   (SELECT p.name FROM entries e JOIN players p ON p.id = e.player_id
                     WHERE e.game_id = g.id AND e.net > 0 ORDER BY e.net DESC, p.name LIMIT 1),
                   (SELECT MAX(e.net) FROM entries e WHERE e.game_id = g.id),
                   (SELECT COUNT(*) FROM transfers t WHERE t.game_id = g.id AND t.is_paid = 0)
            FROM games g
            WHERE ($from IS NULL OR g.date >= $from) AND ($to IS NULL OR g.date <= $to)
              AND ($q IS NULL OR g.title LIKE $q ESCAPE '\' OR g.location LIKE $q ESCAPE '\' OR EXISTS (
                    SELECT 1 FROM entries e JOIN players p ON p.id = e.player_id
                    WHERE e.game_id = g.id AND (p.name LIKE $q ESCAPE '\' OR p.nickname LIKE $q ESCAPE '\')))
            ORDER BY g.date DESC, g.created_at DESC, g.id DESC
            LIMIT $limit
            """,
            r =>
            {
                var status = (GameStatus)r.GetInt64(3);
                var chips = r.GetInt64OrNull(5);
                var money = r.GetInt64OrNull(6);
                return new GameListItem(r.GetInt64(0), r.GetString(1), r.GetDateOnly(2), status, r.GetFlag(4),
                    chips is > 0 && money is > 0 ? new ChipRate(chips.Value, money.Value) : null,
                    (int)r.GetInt64(7), r.GetInt64(8), r.GetStringOrNull(9), r.GetInt64OrNull(10),
                    status == GameStatus.Finalized ? (int)r.GetInt64(11) : 0);
            },
            ("$from", query.From), ("$to", query.To), ("$q", search), ("$limit", query.Limit));
    }

    private static List<PendingPayment> QueryPending(Sql sql) => sql.Query(
        """
        SELECT t.id, t.game_id, g.title, g.date, t.from_id, pf.name, t.to_id, pt.name, pt.upi_id, t.amount
        FROM transfers t
        JOIN games g ON g.id = t.game_id AND g.status = 1
        JOIN players pf ON pf.id = t.from_id
        JOIN players pt ON pt.id = t.to_id
        WHERE t.is_paid = 0
        ORDER BY pf.name COLLATE NOCASE, g.date DESC, t.id
        """,
        r => new PendingPayment(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetDateOnly(3), r.GetInt64(4), r.GetString(5),
            r.GetInt64(6), r.GetString(7), r.GetStringOrNull(8), r.GetInt64(9)));

    private static GameSnapshot? LoadSnapshot(Sql sql, long gameId)
    {
        var game = sql.QuerySingle($"SELECT {Rows.GameColumns} FROM games g WHERE g.id = $id", r => Rows.Game(r), ("$id", gameId));
        if (game is null) return null;
        var entries = sql.Query(
            $"SELECT {Rows.EntryColumns}, {Rows.PlayerColumns} FROM entries e JOIN players p ON p.id = e.player_id WHERE e.game_id = $id ORDER BY e.position, e.player_id",
            r => new GameEntry(Rows.Entry(r), Rows.Player(r, 5)), ("$id", gameId));
        var buyIns = sql.Query($"SELECT {Rows.BuyInColumns} FROM buy_ins b WHERE b.game_id = $id ORDER BY b.at, b.id", Rows.BuyIn, ("$id", gameId));
        var adjustments = sql.Query($"SELECT {Rows.AdjustmentColumns} FROM adjustments a WHERE a.game_id = $id ORDER BY a.id", Rows.Adjustment, ("$id", gameId));
        var transfers = sql.Query($"SELECT {Rows.TransferColumns} FROM transfers t WHERE t.game_id = $id ORDER BY t.id", Rows.Transfer, ("$id", gameId));
        return new GameSnapshot(game, entries, buyIns, adjustments, transfers);
    }

    private Game EnsureInProgress(Sql sql, long gameId)
    {
        var game = sql.QuerySingle($"SELECT {Rows.GameColumns} FROM games g WHERE g.id = $id", r => Rows.Game(r), ("$id", gameId))
            ?? throw new InvalidOperationException("Game not found.");
        if (game.Status != GameStatus.InProgress) throw new GameNotEditableException();
        return game;
    }

    private void Touch(Sql sql, long gameId) =>
        sql.Execute("UPDATE games SET updated_at = $now WHERE id = $id", ("$now", _clock.Now), ("$id", gameId));

    private static void InsertEntry(Sql sql, long gameId, long playerId, int position) =>
        sql.Execute("INSERT INTO entries (game_id, player_id, position, cash_out, net) VALUES ($g, $p, $pos, NULL, NULL)",
            ("$g", gameId), ("$p", playerId), ("$pos", position));

    private static BuyIn InsertBuyIn(Sql sql, long gameId, long playerId, long amount, DateTimeOffset at)
    {
        var id = sql.Insert("INSERT INTO buy_ins (game_id, player_id, amount, at) VALUES ($g, $p, $a, $at) RETURNING id",
            ("$g", gameId), ("$p", playerId), ("$a", amount), ("$at", at));
        return new BuyIn(id, gameId, playerId, amount, DateTimeOffset.FromUnixTimeMilliseconds(at.ToUnixTimeMilliseconds()));
    }

    /// <summary>Inserts a whole game with its original ids (restore and import).</summary>
    private static void InsertGameRecord(Sql sql, GameRecord record)
    {
        var g = record.Game;
        var s = g.Settings;
        sql.Execute(
            """
            INSERT INTO games (id, title, date, location, notes, status, default_buy_in, chip_mode, chip_rate_chips, chip_rate_money,
                               settlement_mode, banker_id, rounding, start_with_buy_in, created_at, updated_at, finalized_at)
            VALUES ($id, $title, $date, $loc, $notes, $status, $buyin, $chip, $rc, $rm, $mode, $banker, $round, $start, $created, $updated, $final)
            """,
            ("$id", g.Id), ("$title", g.Title), ("$date", g.Date), ("$loc", g.Location), ("$notes", g.Notes), ("$status", g.Status),
            ("$buyin", s.DefaultBuyIn), ("$chip", s.ChipMode), ("$rc", s.ChipRate?.Chips), ("$rm", s.ChipRate?.Money),
            ("$mode", s.SettlementMode), ("$banker", s.BankerId), ("$round", s.Rounding), ("$start", s.StartWithBuyIn),
            ("$created", g.CreatedAt), ("$updated", g.UpdatedAt), ("$final", g.FinalizedAt));

        using (var entry = sql.Command("INSERT INTO entries (game_id, player_id, position, cash_out, net) VALUES ($g, $p, $pos, $c, $n)"))
        {
            foreach (var e in record.Entries)
            {
                Bind(entry, ("$g", e.GameId), ("$p", e.PlayerId), ("$pos", e.Position), ("$c", e.CashOut), ("$n", e.Net));
                entry.ExecuteNonQuery();
            }
        }
        using (var buyIn = sql.Command("INSERT INTO buy_ins (id, game_id, player_id, amount, at) VALUES ($id, $g, $p, $a, $at)"))
        {
            foreach (var b in record.BuyIns)
            {
                Bind(buyIn, ("$id", b.Id), ("$g", b.GameId), ("$p", b.PlayerId), ("$a", b.Amount), ("$at", b.At));
                buyIn.ExecuteNonQuery();
            }
        }
        foreach (var a in record.Adjustments)
        {
            sql.Execute("INSERT INTO adjustments (id, game_id, kind, player_id, amount) VALUES ($id, $g, $k, $p, $a)",
                ("$id", a.Id), ("$g", a.GameId), ("$k", a.Kind), ("$p", a.PlayerId), ("$a", a.Amount));
        }
        foreach (var t in record.Transfers)
        {
            sql.Execute("INSERT INTO transfers (id, game_id, from_id, to_id, amount, is_paid, paid_at) VALUES ($id, $g, $f, $t, $a, $paid, $at)",
                ("$id", t.Id), ("$g", t.GameId), ("$f", t.FromId), ("$t", t.ToId), ("$a", t.Amount), ("$paid", t.IsPaid), ("$at", t.PaidAt));
        }
    }

    /// <summary>Rebinds a prepared command's parameters (bulk inserts reuse one statement).</summary>
    private static void Bind(SqliteCommand command, params (string Name, object? Value)[] args)
    {
        command.Parameters.Clear();
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, Sql.ToDb(value));
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
