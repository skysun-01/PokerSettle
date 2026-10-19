using PokerSettle.Core.Money;

namespace PokerSettle.Core.Stats;

/// <summary>Everything the cumulative profit chart draws, with labels already formatted.</summary>
public sealed record ChartData(
    IReadOnlyList<ChartPoint> Points,
    long Min,
    long Max,
    string MaxLabel,
    string MinLabel,
    string StartLabel,
    string EndLabel,
    string Description)
{
    public static ChartData Empty { get; } = new([], 0, 0, "", "", "", "", "No finished games yet.");

    public bool HasPoints => Points.Count > 0;

    public static ChartData From(IReadOnlyList<PlayerGameRow> games, MoneyFormatter money)
    {
        var points = PlayerStatsMath.Cumulative(games);
        if (points.Count == 0) return Empty;
        // The axis always includes zero so "up or down overall" is visible at a glance.
        var max = Math.Max(0, points.Max(p => p.Cumulative));
        var min = Math.Min(0, points.Min(p => p.Cumulative));
        var final = points[^1].Cumulative;
        var description = $"Cumulative result over {points.Count} {(points.Count == 1 ? "game" : "games")}: "
            + $"lowest {money.FormatSigned(points.Min(p => p.Cumulative))}, highest {money.FormatSigned(points.Max(p => p.Cumulative))}, "
            + $"now {money.FormatSigned(final)}.";
        return new ChartData(points, min, max, money.FormatSigned(max), money.FormatSigned(min),
            money.FormatShortDate(points[0].Date), money.FormatShortDate(points[^1].Date), description);
    }
}
