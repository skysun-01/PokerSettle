using PokerSettle.Core.Models;

namespace PokerSettle.Core.Data;

public sealed class DatabaseOptions
{
    public required string DirectoryPath { get; init; }
    public string FileName { get; init; } = "poker-settle.db";
    /// <summary>Separate file used by "Use demo database" in Debug builds; real data is never touched.</summary>
    public string DemoFileName { get; init; } = "poker-settle-demo.db";
    public bool StartWithDemo { get; init; }
    public int SnapshotsToKeep { get; init; } = 5;

    public string SnapshotsDirectory => Path.Combine(DirectoryPath, "snapshots");
}

/// <summary>Why the database was replaced at startup and where the old file went.</summary>
public sealed record RecoveryInfo(string Reason, string? MovedTo, DateTimeOffset At, bool InMemory);

public sealed record DatabaseInfo(
    string Path,
    long SizeBytes,
    int SchemaVersion,
    int Players,
    int ArchivedPlayers,
    int Games,
    int GamesInProgress,
    int Entries,
    int BuyIns,
    int Transfers,
    int UnpaidTransfers,
    int Snapshots);

public sealed record PlayerDraft(string Name, string? Nickname, string? Phone, string? UpiId, string? Notes, string? Color);

public sealed record PlayerListItem(Player Player, int GamesPlayed, DateOnly? LastPlayed);

public sealed record NewGameRequest(
    string Title,
    DateOnly Date,
    string? Location,
    string? Notes,
    GameSettings Settings,
    IReadOnlyList<long> PlayerIds);

public sealed record GameQuery(string? Search = null, DateOnly? From = null, DateOnly? To = null, int Limit = 5000);

public sealed record GameListItem(
    long Id,
    string Title,
    DateOnly Date,
    GameStatus Status,
    bool ChipMode,
    ChipRate? ChipRate,
    int PlayerCount,
    long Pot,
    string? TopWinnerName,
    long? TopWinnerNet,
    int UnpaidCount);

public sealed record InProgressGame(long Id, string Title, DateOnly Date, bool ChipMode, ChipRate? ChipRate, int PlayerCount, long Pot);

public sealed record PendingPayment(
    long TransferId,
    long GameId,
    string GameTitle,
    DateOnly GameDate,
    long FromId,
    string FromName,
    long ToId,
    string ToName,
    string? ToUpiId,
    long Amount);

public sealed record HomeData(
    InProgressGame? InProgress,
    IReadOnlyList<GameListItem> RecentGames,
    IReadOnlyList<Stats.BoardRow> Board,
    IReadOnlyList<PendingPayment> PendingPayments,
    int FinalizedGames,
    int GamesSinceBackup,
    int PlayerCount);

public sealed record FinalizeRequest(
    DiscrepancyResolution Resolution,
    long? AssignedPlayerId,
    SettlementMode Mode,
    long? BankerId,
    RoundingMode Rounding);

/// <summary>A player removed from a live game, kept so Undo can put them back.</summary>
public sealed record RemovedEntry(Entry Entry, IReadOnlyList<BuyIn> BuyIns);

/// <summary>A whole game as plain rows (export/import, Undo delete).</summary>
public sealed record GameRecord(
    Game Game,
    IReadOnlyList<Entry> Entries,
    IReadOnlyList<BuyIn> BuyIns,
    IReadOnlyList<Adjustment> Adjustments,
    IReadOnlyList<Transfer> Transfers)
{
    public static GameRecord From(GameSnapshot s) =>
        new(s.Game, s.Entries.Select(e => e.Entry).ToList(), s.BuyIns, s.Adjustments, s.Transfers);
}

public sealed record PlayerHistory(Player Player, IReadOnlyList<Stats.PlayerGameRow> Games);

public sealed class DuplicatePlayerNameException(string name, long existingId)
    : Exception($"A player named \"{name}\" already exists.")
{
    public long ExistingPlayerId { get; } = existingId;
}

public sealed class GameInProgressException(long gameId)
    : Exception("Another game is already in progress. Finish it first.")
{
    public long GameId { get; } = gameId;
}

public sealed class GameNotEditableException()
    : Exception("This game is finalized. Reopen it to make changes.");

public sealed class SettlementValidationException(IReadOnlyList<Settlement.SettlementIssue> issues)
    : Exception("The game can't be finalized yet: " + string.Join(", ", issues.Select(i => i.Kind)))
{
    public IReadOnlyList<Settlement.SettlementIssue> Issues { get; } = issues;
}

/// <summary>The database file is unusable (corrupt, failed integrity check, or from a newer app).</summary>
public sealed class DatabaseUnusableException(string reason) : Exception(reason);
