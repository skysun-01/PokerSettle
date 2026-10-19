namespace PokerSettle.Core.Stats;

/// <summary>Aggregated results for one player over finalized games in a date range. Money in paise.</summary>
public sealed record BoardRow(
    long PlayerId,
    string Name,
    string? Nickname,
    string Color,
    bool IsArchived,
    int Games,
    long Net,
    long Won,
    long Lost,
    int Wins,
    long? BiggestWin,
    long? BiggestLoss,
    DateOnly? LastPlayed)
{
    /// <summary>Fraction of nights in profit (0..1).</summary>
    public double WinRate => Games == 0 ? 0 : (double)Wins / Games;

    /// <summary>Average result per game, rounded to the nearest paisa.</summary>
    public long Average => Games == 0 ? 0 : (long)Math.Round((decimal)Net / Games, MidpointRounding.AwayFromZero);
}

public enum BoardColumn
{
    Net = 0,
    Name = 1,
    Won = 2,
    Lost = 3,
    Games = 4,
    WinRate = 5,
    Average = 6,
    BiggestWin = 7,
    BiggestLoss = 8,
    LastPlayed = 9,
}

public sealed record RankedBoardRow(int Rank, BoardRow Row);

public static class BoardMath
{
    /// <summary>Competition ranking by net (1, 2, 2, 4), independent of the display sort.</summary>
    public static IReadOnlyList<RankedBoardRow> RankByNet(IEnumerable<BoardRow> rows)
    {
        var ordered = rows
            .OrderByDescending(r => r.Net)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PlayerId)
            .ToList();
        var result = new List<RankedBoardRow>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var rank = i > 0 && ordered[i].Net == ordered[i - 1].Net ? result[i - 1].Rank : i + 1;
            result.Add(new RankedBoardRow(rank, ordered[i]));
        }
        return result;
    }

    /// <summary>Sorts by a column; ties fall back to net (highest first), then name, then id.</summary>
    public static IReadOnlyList<RankedBoardRow> Sort(IEnumerable<RankedBoardRow> rows, BoardColumn column, bool descending)
    {
        IOrderedEnumerable<RankedBoardRow> ordered = column switch
        {
            BoardColumn.Name => By(rows, r => r.Row.Name.ToUpperInvariant(), descending),
            BoardColumn.Won => By(rows, r => r.Row.Won, descending),
            BoardColumn.Lost => By(rows, r => r.Row.Lost, descending),
            BoardColumn.Games => By(rows, r => r.Row.Games, descending),
            BoardColumn.WinRate => By(rows, r => r.Row.WinRate, descending),
            BoardColumn.Average => By(rows, r => r.Row.Average, descending),
            BoardColumn.BiggestWin => By(rows, r => r.Row.BiggestWin ?? long.MinValue, descending),
            BoardColumn.BiggestLoss => By(rows, r => r.Row.BiggestLoss ?? long.MaxValue, descending),
            BoardColumn.LastPlayed => By(rows, r => r.Row.LastPlayed ?? DateOnly.MinValue, descending),
            _ => By(rows, r => r.Row.Net, descending),
        };
        return ordered
            .ThenByDescending(r => r.Row.Net)
            .ThenBy(r => r.Row.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Row.PlayerId)
            .ToList();

        static IOrderedEnumerable<RankedBoardRow> By<TKey>(IEnumerable<RankedBoardRow> source, Func<RankedBoardRow, TKey> key, bool desc) =>
            desc ? source.OrderByDescending(key) : source.OrderBy(key);
    }

    /// <summary>The natural first direction when a column header is tapped.</summary>
    public static bool DefaultDescending(BoardColumn column) => column switch
    {
        BoardColumn.Name => false,
        // Biggest loss is most negative first, i.e. ascending.
        BoardColumn.BiggestLoss => false,
        BoardColumn.Lost => false,
        _ => true,
    };
}

/// <summary>One finalized game in a player's history, oldest first.</summary>
public sealed record PlayerGameRow(long GameId, DateOnly Date, string Title, long Net, long BuyInTotal, bool ChipMode);

public sealed record ChartPoint(int Index, DateOnly Date, long Cumulative, long Net);

public static class PlayerStatsMath
{
    /// <summary>Builds the board row for one player from their history (same math as the board query).</summary>
    public static BoardRow Summarize(long playerId, string name, string? nickname, string color, bool archived, IReadOnlyList<PlayerGameRow> games)
    {
        long net = 0, won = 0, lost = 0;
        var wins = 0;
        long? best = null, worst = null;
        DateOnly? last = null;
        foreach (var g in games)
        {
            net += g.Net;
            if (g.Net > 0)
            {
                won += g.Net;
                wins++;
            }
            else if (g.Net < 0)
            {
                lost += g.Net;
            }
            best = best is null ? g.Net : Math.Max(best.Value, g.Net);
            worst = worst is null ? g.Net : Math.Min(worst.Value, g.Net);
            if (last is null || g.Date > last) last = g.Date;
        }
        return new BoardRow(playerId, name, nickname, color, archived, games.Count, net, won, lost, wins,
            best is > 0 ? best : null, worst is < 0 ? worst : null, last);
    }

    public static IReadOnlyList<ChartPoint> Cumulative(IReadOnlyList<PlayerGameRow> games)
    {
        var points = new List<ChartPoint>(games.Count);
        long running = 0;
        for (var i = 0; i < games.Count; i++)
        {
            running += games[i].Net;
            points.Add(new ChartPoint(i, games[i].Date, running, games[i].Net));
        }
        return points;
    }
}
