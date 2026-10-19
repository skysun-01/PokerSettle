using System.Globalization;
using Microsoft.Data.Sqlite;
using PokerSettle.Core.Models;

namespace PokerSettle.Core.Data;

/// <summary>
/// A unit of work on the single connection: optional transaction plus small helpers.
/// Only DataStore creates these, so all SQL lives in the data layer.
/// </summary>
internal sealed class Sql(SqliteConnection connection, SqliteTransaction? transaction)
{
    public SqliteConnection Connection { get; } = connection;
    public SqliteTransaction? Transaction { get; } = transaction;

    public SqliteCommand Command(string text, params (string Name, object? Value)[] args)
    {
        var command = Connection.CreateCommand();
        command.CommandText = text;
        command.Transaction = Transaction;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, ToDb(value));
        return command;
    }

    public int Execute(string text, params (string Name, object? Value)[] args)
    {
        using var command = Command(text, args);
        return command.ExecuteNonQuery();
    }

    public object? Scalar(string text, params (string Name, object? Value)[] args)
    {
        using var command = Command(text, args);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public long ScalarLong(string text, params (string Name, object? Value)[] args) =>
        Convert.ToInt64(Scalar(text, args) ?? 0L, CultureInfo.InvariantCulture);

    public long? ScalarLongOrNull(string text, params (string Name, object? Value)[] args) =>
        Scalar(text, args) is { } value ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;

    /// <summary>Runs an INSERT … RETURNING id and returns the id.</summary>
    public long Insert(string text, params (string Name, object? Value)[] args) => ScalarLong(text, args);

    public List<T> Query<T>(string text, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args)
    {
        using var command = Command(text, args);
        using var reader = command.ExecuteReader();
        var list = new List<T>();
        while (reader.Read()) list.Add(map(reader));
        return list;
    }

    public T? QuerySingle<T>(string text, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] args) where T : class
    {
        using var command = Command(text, args);
        using var reader = command.ExecuteReader();
        return reader.Read() ? map(reader) : null;
    }

    public static object ToDb(object? value) => value switch
    {
        null => DBNull.Value,
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset t => t.ToUnixTimeMilliseconds(),
        bool b => b ? 1L : 0L,
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture),
        _ => value,
    };
}

internal static class ReaderExtensions
{
    public static string? GetStringOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    public static long? GetInt64OrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    public static DateOnly GetDateOnly(this SqliteDataReader r, int i) =>
        DateOnly.ParseExact(r.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly? GetDateOnlyOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateOnly(i);

    public static DateTimeOffset GetTimestamp(this SqliteDataReader r, int i) => DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(i));

    public static DateTimeOffset? GetTimestampOrNull(this SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetTimestamp(i);

    public static bool GetFlag(this SqliteDataReader r, int i) => r.GetInt64(i) != 0;
}

/// <summary>Column lists and row mappers shared by the DataStore partials.</summary>
internal static class Rows
{
    public const string PlayerColumns =
        "p.id, p.name, p.nickname, p.phone, p.upi_id, p.notes, p.color, p.is_archived, p.created_at";

    public const int PlayerColumnCount = 9;

    public const string GameColumns =
        "g.id, g.title, g.date, g.location, g.notes, g.status, g.default_buy_in, g.chip_mode, g.chip_rate_chips, " +
        "g.chip_rate_money, g.settlement_mode, g.banker_id, g.rounding, g.start_with_buy_in, g.created_at, g.updated_at, g.finalized_at";

    public const int GameColumnCount = 17;

    public static Player Player(SqliteDataReader r, int o = 0) => new()
    {
        Id = r.GetInt64(o),
        Name = r.GetString(o + 1),
        Nickname = r.GetStringOrNull(o + 2),
        Phone = r.GetStringOrNull(o + 3),
        UpiId = r.GetStringOrNull(o + 4),
        Notes = r.GetStringOrNull(o + 5),
        Color = r.GetString(o + 6),
        IsArchived = r.GetFlag(o + 7),
        CreatedAt = r.GetTimestamp(o + 8),
    };

    public static Game Game(SqliteDataReader r, int o = 0)
    {
        var rateChips = r.GetInt64OrNull(o + 8);
        var rateMoney = r.GetInt64OrNull(o + 9);
        return new Game
        {
            Id = r.GetInt64(o),
            Title = r.GetString(o + 1),
            Date = r.GetDateOnly(o + 2),
            Location = r.GetStringOrNull(o + 3),
            Notes = r.GetStringOrNull(o + 4),
            Status = (GameStatus)r.GetInt64(o + 5),
            Settings = new GameSettings
            {
                DefaultBuyIn = r.GetInt64(o + 6),
                ChipMode = r.GetFlag(o + 7),
                ChipRate = rateChips is > 0 && rateMoney is > 0 ? new ChipRate(rateChips.Value, rateMoney.Value) : null,
                SettlementMode = (SettlementMode)r.GetInt64(o + 10),
                BankerId = r.GetInt64OrNull(o + 11),
                Rounding = (RoundingMode)r.GetInt64(o + 12),
                StartWithBuyIn = r.GetFlag(o + 13),
            },
            CreatedAt = r.GetTimestamp(o + 14),
            UpdatedAt = r.GetTimestamp(o + 15),
            FinalizedAt = r.GetTimestampOrNull(o + 16),
        };
    }

    public const string EntryColumns = "e.game_id, e.player_id, e.position, e.cash_out, e.net";

    public static Entry Entry(SqliteDataReader r, int o = 0) => new()
    {
        GameId = r.GetInt64(o),
        PlayerId = r.GetInt64(o + 1),
        Position = (int)r.GetInt64(o + 2),
        CashOut = r.GetInt64OrNull(o + 3),
        Net = r.GetInt64OrNull(o + 4),
    };

    public const string BuyInColumns = "b.id, b.game_id, b.player_id, b.amount, b.at";

    public static BuyIn BuyIn(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetTimestamp(4));

    public const string AdjustmentColumns = "a.id, a.game_id, a.kind, a.player_id, a.amount";

    public static Adjustment Adjustment(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetInt64(1), (AdjustmentKind)r.GetInt64(2), r.GetInt64OrNull(3), r.GetInt64(4));

    public const string TransferColumns = "t.id, t.game_id, t.from_id, t.to_id, t.amount, t.is_paid, t.paid_at";

    public static Transfer Transfer(SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4), r.GetFlag(5), r.GetTimestampOrNull(6));
}
