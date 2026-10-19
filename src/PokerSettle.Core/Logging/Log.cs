using Microsoft.Extensions.Logging;

namespace PokerSettle.Core.Logging;

/// <summary>
/// Every log message in the app, as source-generated [LoggerMessage] methods. When debug logging is
/// off they return after a single IsEnabled check: no formatting, no allocation.
/// Never pass phone numbers or UPI IDs to these methods.
/// </summary>
public static partial class Log
{
    // ---- App lifecycle (1xx)
    [LoggerMessage(100, LogLevel.Information, "App started: version {AppVersion}, schema {SchemaVersion}, {Platform} {Device}, OS {Os}, database {DatabaseBytes} bytes, demo {IsDemo}")]
    public static partial void AppStarted(this ILogger logger, string appVersion, int schemaVersion, string platform, string device, string os, long databaseBytes, bool isDemo);

    [LoggerMessage(101, LogLevel.Debug, "App went to the background")]
    public static partial void AppBackgrounded(this ILogger logger);

    [LoggerMessage(102, LogLevel.Debug, "App resumed")]
    public static partial void AppResumed(this ILogger logger);

    [LoggerMessage(103, LogLevel.Warning, "The previous session ended unexpectedly")]
    public static partial void PreviousSessionCrashed(this ILogger logger);

    [LoggerMessage(104, LogLevel.Information, "Setting changed: {Setting} = {Value}")]
    public static partial void SettingChanged(this ILogger logger, string setting, string value);

    [LoggerMessage(105, LogLevel.Information, "Debug logging turned on")]
    public static partial void DebugLoggingEnabled(this ILogger logger);

    [LoggerMessage(106, LogLevel.Debug, "Navigated to {Route}")]
    public static partial void Navigated(this ILogger logger, string route);

    // ---- Database (2xx)
    [LoggerMessage(200, LogLevel.Information, "Database opened in {DurationMs} ms (schema {SchemaVersion}, {DatabaseBytes} bytes)")]
    public static partial void DatabaseOpened(this ILogger logger, long durationMs, int schemaVersion, long databaseBytes);

    [LoggerMessage(201, LogLevel.Debug, "DB {Operation} took {DurationMs} ms")]
    public static partial void DatabaseOperation(this ILogger logger, string operation, double durationMs);

    [LoggerMessage(202, LogLevel.Information, "Migration {Version} applied: {Description} ({DurationMs} ms)")]
    public static partial void MigrationApplied(this ILogger logger, int version, string description, long durationMs);

    [LoggerMessage(203, LogLevel.Error, "Database could not be used ({Reason}); moved aside to {MovedTo}")]
    public static partial void DatabaseRecovered(this ILogger logger, Exception? exception, string reason, string movedTo);

    [LoggerMessage(204, LogLevel.Information, "Snapshot saved: {FileName} ({Bytes} bytes, {DurationMs} ms)")]
    public static partial void SnapshotCreated(this ILogger logger, string fileName, long bytes, long durationMs);

    [LoggerMessage(205, LogLevel.Error, "DB {Operation} failed")]
    public static partial void DatabaseOperationFailed(this ILogger logger, Exception exception, string operation);

    [LoggerMessage(206, LogLevel.Information, "Switched database to {FileName}")]
    public static partial void DatabaseSwitched(this ILogger logger, string fileName);

    [LoggerMessage(207, LogLevel.Information, "{Operation} took {DurationMs} ms")]
    public static partial void Timing(this ILogger logger, string operation, long durationMs);

    // ---- Validation (3xx)
    [LoggerMessage(300, LogLevel.Information, "Validation failed in {Context}: {Reason}")]
    public static partial void ValidationFailed(this ILogger logger, string context, string reason);

    // ---- Settlement (4xx)
    [LoggerMessage(400, LogLevel.Information, "Settlement for game {GameId}: mode {Mode}, rounding {Rounding}, chips {ChipMode}; nets {Inputs}; payments {Outputs}")]
    public static partial void SettlementComputed(this ILogger logger, long gameId, string mode, string rounding, bool chipMode, string inputs, string outputs);

    // ---- Key actions (5xx)
    [LoggerMessage(500, LogLevel.Information, "Game {GameId} created with {PlayerCount} players (chip mode {ChipMode}, default buy-in {DefaultBuyIn})")]
    public static partial void GameCreated(this ILogger logger, long gameId, int playerCount, bool chipMode, long defaultBuyIn);

    [LoggerMessage(501, LogLevel.Information, "Buy-in {BuyInId}: player {PlayerId} +{Amount} in game {GameId}")]
    public static partial void BuyInAdded(this ILogger logger, long gameId, long playerId, long amount, long buyInId);

    [LoggerMessage(502, LogLevel.Information, "Buy-in {BuyInId} removed from game {GameId}")]
    public static partial void BuyInRemoved(this ILogger logger, long gameId, long buyInId);

    [LoggerMessage(503, LogLevel.Information, "Cash-out for player {PlayerId} in game {GameId}: {Amount}")]
    public static partial void CashOutSet(this ILogger logger, long gameId, long playerId, long? amount);

    [LoggerMessage(504, LogLevel.Information, "Game {GameId} finalized with {PaymentCount} payments, resolution {Resolution}")]
    public static partial void GameFinalized(this ILogger logger, long gameId, int paymentCount, string resolution);

    [LoggerMessage(505, LogLevel.Information, "Game {GameId} reopened")]
    public static partial void GameReopened(this ILogger logger, long gameId);

    [LoggerMessage(506, LogLevel.Information, "Game {GameId} deleted")]
    public static partial void GameDeleted(this ILogger logger, long gameId);

    [LoggerMessage(507, LogLevel.Information, "Game {GameId} restored")]
    public static partial void GameRestored(this ILogger logger, long gameId);

    [LoggerMessage(508, LogLevel.Information, "Player {PlayerId} added to game {GameId}")]
    public static partial void PlayerJoinedGame(this ILogger logger, long gameId, long playerId);

    [LoggerMessage(509, LogLevel.Information, "Player {PlayerId} removed from game {GameId}")]
    public static partial void PlayerLeftGame(this ILogger logger, long gameId, long playerId);

    [LoggerMessage(510, LogLevel.Information, "Player {PlayerId} saved (new {IsNew})")]
    public static partial void PlayerSaved(this ILogger logger, long playerId, bool isNew);

    [LoggerMessage(511, LogLevel.Information, "Player {PlayerId} archived {Archived}")]
    public static partial void PlayerArchived(this ILogger logger, long playerId, bool archived);

    [LoggerMessage(512, LogLevel.Information, "Player {PlayerId} deleted")]
    public static partial void PlayerDeleted(this ILogger logger, long playerId);

    [LoggerMessage(513, LogLevel.Information, "Transfer {TransferId} paid {IsPaid}")]
    public static partial void TransferPaid(this ILogger logger, long transferId, bool isPaid);

    [LoggerMessage(514, LogLevel.Information, "Export ({Target}): {PlayerCount} players, {GameCount} games, {Bytes} bytes in {DurationMs} ms")]
    public static partial void ExportCompleted(this ILogger logger, string target, int playerCount, int gameCount, long bytes, long durationMs);

    [LoggerMessage(515, LogLevel.Information, "Import: {PlayerCount} players, {GameCount} games in {DurationMs} ms")]
    public static partial void ImportCompleted(this ILogger logger, int playerCount, int gameCount, long durationMs);

    [LoggerMessage(516, LogLevel.Warning, "Import rejected: {Reason}")]
    public static partial void ImportRejected(this ILogger logger, string reason);

    [LoggerMessage(517, LogLevel.Warning, "All data cleared")]
    public static partial void DataCleared(this ILogger logger);

    [LoggerMessage(518, LogLevel.Information, "Players {SourceId} merged into {TargetId}")]
    public static partial void PlayersMerged(this ILogger logger, long sourceId, long targetId);

    [LoggerMessage(519, LogLevel.Information, "Undo: {Action}")]
    public static partial void UndoPerformed(this ILogger logger, string action);

    [LoggerMessage(520, LogLevel.Information, "Game {GameId} details updated")]
    public static partial void GameUpdated(this ILogger logger, long gameId);

    // ---- Errors (9xx)
    [LoggerMessage(900, LogLevel.Error, "Command {Command} failed")]
    public static partial void CommandFailed(this ILogger logger, Exception exception, string command);

    [LoggerMessage(901, LogLevel.Critical, "Unhandled exception from {Source}")]
    public static partial void UnhandledException(this ILogger logger, Exception exception, string source);

    [LoggerMessage(902, LogLevel.Error, "Unobserved task exception")]
    public static partial void UnobservedTaskException(this ILogger logger, Exception exception);

    [LoggerMessage(903, LogLevel.Warning, "Platform service {Service} failed")]
    public static partial void PlatformServiceFailed(this ILogger logger, Exception exception, string service);
}
